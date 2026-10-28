using AslHelp.Memory;
using AslHelp.Memory.Patching;
using NUnit.Framework;

namespace AslHelp.Memory.Tests.Patching;

// Tests the pool at the edges of its block and of the address range, where the 64-bit arithmetic goes wrong first.
[TestFixture]
public class PatchPoolBoundaryTests
{
    // A protect that fails returns its own error even when the release after it fails too.
    [Test]
    public void Create_AProtectAndReleaseThatBothFail_ReturnsTheProtectError()
    {
        var protectError = FakePatchWriter.CreateError("protect");
        FakePatchWriter writer = new()
        {
            ProtectError = protectError,
            ReleaseError = FakePatchWriter.CreateError("release"),
        };

        var result = PatchPool.Create(writer, Address.Zero);

        Address[] released = [writer.NextBase];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error, Is.SameAs(protectError));
            Assert.That(writer.Released, Is.EqualTo(released));
        }
    }

    // Reserving writes no code, since the pool only lays out the block and each patch writes its own stub.
    [Test]
    public void Create_AWriterThatReserves_WritesNoCode()
    {
        FakePatchWriter writer = new();

        _ = PatchPool.Create(writer, Address.Zero).Unwrap();

        Assert.That(writer.Written, Is.Empty);
    }

    // The last record ends exactly where the code pages begin, so no record shares a page with a stub.
    [Test]
    public void TryRent_TheLastRecord_EndsWhereTheCodeBegins()
    {
        var pool = Pool(new(0x1_0000_0000));

        var lastRecord = Address.Zero;
        var firstSlot = Address.Zero;

        while (pool.TryRent(out var record, out var slot))
        {
            if (firstSlot == Address.Zero)
            {
                firstSlot = slot;
            }

            lastRecord = record;
        }

        Assert.That(lastRecord + CaptureLayout.RecordSize, Is.EqualTo(firstSlot));
    }

    // A full pool that gets its last slot back has room for 1 more and hands that slot out again.
    [Test]
    public void Return_TheLastSlotOfAFullPool_MakesItRentableAgain()
    {
        var pool = Pool(new(0x1_0000_0000));

        var last = (Record: Address.Zero, Slot: Address.Zero);

        while (pool.TryRent(out var record, out var slot))
        {
            last = (record, slot);
        }

        pool.Return(last.Slot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool.IsFull, Is.False);
            Assert.That(pool.TryRent(out var record, out var slot), Is.True);
            Assert.That((record, slot), Is.EqualTo(last));
            Assert.That(pool.IsFull, Is.True);
        }
    }

    // Slots come back in reverse order 1 at a time, so a caller can unwind several rents.
    [Test]
    public void Return_EachSlotInReverseOrder_GivesBackEveryOne()
    {
        var pool = Pool(new(0x1_0000_0000));

        var first = Rent(pool);
        var second = Rent(pool);

        pool.Return(second.Slot);
        pool.Return(first.Slot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => pool.Return(first.Slot), Throws.ArgumentException);
            Assert.That(Rent(pool), Is.EqualTo(first));
        }
    }

    // A refused slot shows up in the message as hex. The exception points at the slot parameter.
    [Test]
    public void Return_ASlotOnAFreshPool_NamesItInTheMessage()
    {
        var pool = Pool(new(0x1_0000_0000));

        Assert.That(
            () => pool.Return(new Address(0x1_0000_6000)),
            Throws.ArgumentException
                .With.Message.Contains("0x100006000")
                .And.Property("ParamName").EqualTo("slot"));
    }

    // Every record and slot stays inside the 64 KB block when the base sits at the top of 32-bit space.
    [Test]
    public void Contains_ABaseAtTheTopOf32BitSpace_IsTrueForEverySlot()
    {
        var pool = Pool(new(0xFFFF_0000));

        while (pool.TryRent(out var record, out var slot))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pool.Contains(record), Is.True);
                Assert.That(pool.Contains(slot + (CodePatch.SlotSize - 1)), Is.True);
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool.Contains(new Address(0xFFFF_FFFF)), Is.True);
            Assert.That(pool.Contains(new Address(0x1_0000_0000)), Is.False);
        }
    }

    // An entry just below a base at the top of 32-bit space is in range, since the end bound does not wrap at 4 GB.
    [Test]
    public void CanReach_AnEntryBelowABaseAtTheTopOf32BitSpace_IsTrue()
    {
        var pool = Pool(new(0xFFFF_0000));

        Assert.That(pool.CanReach(new Address(0x8001_0000)), Is.True);
    }

    // A base whose end wraps past the top contains every slot, not just the ones below the wrap.
    [Test]
    public void Contains_ABaseWhoseEndWrapsPastTheTop_IsTrueForEverySlot()
    {
        var pool = Pool(new(ulong.MaxValue - 0xFFFF));

        while (pool.TryRent(out var record, out var slot))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pool.Contains(record), Is.True);
                Assert.That(pool.Contains(slot), Is.True);
            }
        }

        Assert.That(pool.Contains(Address.Zero), Is.False);
    }

    // An address 1 byte below a base at 0 wraps to the top and is not contained.
    [Test]
    public void Contains_TheByteBelowABaseAtZero_IsFalse()
    {
        var pool = Pool(Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool.Contains(Address.Zero), Is.True);
            Assert.That(pool.Contains(new Address(ulong.MaxValue)), Is.False);
        }
    }

    // An entry at the base itself is in range, the 0 distance being the closest a patch can get.
    [Test]
    public void CanReach_AnEntryAtTheBase_IsTrue()
    {
        var pool = Pool(new(0x1_0000_0000));

        Assert.That(pool.CanReach(pool.Base), Is.True);
    }

    private static PatchPool Pool(Address @base)
    {
        FakePatchWriter writer = new() { NextBase = @base };

        return PatchPool.Create(writer, Address.Zero).Unwrap();
    }

    // Asserts the rent succeeded before returning the pair, since a slot and its record mean nothing otherwise.
    private static (Address Record, Address Slot) Rent(PatchPool pool)
    {
        Assert.That(pool.TryRent(out var record, out var slot), Is.True);

        return (record, slot);
    }
}
