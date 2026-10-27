using AslHelp.Memory;
using AslHelp.Memory.Patching;
using NUnit.Framework;
using System;

namespace AslHelp.Memory.Tests.Patching;

// Tests the pool against a fake writer, so nothing here reserves memory, writes code or suspends a thread. The pool
// is pointer-width agnostic: the width lives in the writer's Reserve, so every address below is just a number.
[TestFixture]
public class PatchPoolTests
{
    // Reserving asks the writer for 1 block and makes only the record pages read-write.
    [Test]
    public void Create_AWriterThatReserves_MakesTheRecordPagesReadWrite()
    {
        var anchor = new Address(0x1_4000_0000);
        FakePatchWriter writer = new() { NextBase = new(0x1_0000_0000) };

        var pool = PatchPool.Create(writer, anchor).Unwrap();

        (Address, int)[] reserved = [(anchor, 0x10000)];
        (Address, int, MemoryProtection)[] protectionChanges = [(writer.NextBase, 0x6000, MemoryProtection.Read | MemoryProtection.Write)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Reserved, Is.EqualTo(reserved));
            Assert.That(writer.ProtectionChanges, Is.EqualTo(protectionChanges));
            Assert.That(writer.Released, Is.Empty);
            Assert.That(pool.Base, Is.EqualTo(writer.NextBase));
        }
    }

    // A null writer is refused before anything is asked of it.
    [Test]
    public void Create_ANullWriter_Throws()
    {
        Assert.That(() => PatchPool.Create(null!, Address.Zero), Throws.ArgumentNullException);
    }

    // A reservation that fails returns its own error and leaves nothing protected.
    [Test]
    public void Create_AReserveThatFails_ReturnsTheErrorAndProtectsNothing()
    {
        var error = FakePatchWriter.CreateError();
        FakePatchWriter writer = new() { ReserveError = error };

        var result = PatchPool.Create(writer, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(writer.ProtectionChanges, Is.Empty);
            Assert.That(writer.Released, Is.Empty);
        }
    }

    // A protect that fails releases the block before returning, so a failed pool leaks no 64 KB reservation.
    [Test]
    public void Create_AProtectThatFails_ReleasesTheReservationAndReturnsTheError()
    {
        var error = FakePatchWriter.CreateError();
        FakePatchWriter writer = new() { NextBase = new(0x1_0000_0000), ProtectError = error };

        var result = PatchPool.Create(writer, Address.Zero);

        (Address, int, MemoryProtection)[] protectionChanges = [(writer.NextBase, 0x6000, MemoryProtection.Read | MemoryProtection.Write)];
        Address[] released = [writer.NextBase];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(writer.ProtectionChanges, Is.EqualTo(protectionChanges));
            Assert.That(writer.Released, Is.EqualTo(released));
        }
    }

    // Each rent hands out a record on its own pages and a stub a slot apart from the last.
    [Test]
    public void TryRent_EverySlot_RentsARecordOfItsOwn()
    {
        var pool = Pool();

        var first = Rent(pool);
        var second = Rent(pool);

        using (Assert.EnterMultipleScope())
        {
            // A record is 1 whole header-and-payload apart from the next, so no capture reaches the next count.
            Assert.That(second.Record - first.Record, Is.EqualTo((long)CaptureLayout.RecordSize));

            // The stub sits past every record page, so a stub writing its record never touches the page it runs from.
            Assert.That(first.Slot - first.Record, Is.EqualTo(0x6000L));
            Assert.That(second.Slot - first.Slot, Is.EqualTo((long)CodePatch.SlotSize));

            Assert.That(pool.Contains(first.Record), Is.True);
            Assert.That(pool.Contains(second.Slot + (CodePatch.SlotSize - 1)), Is.True);
        }
    }

    // A fresh pool has every slot free.
    [Test]
    public void IsFull_AFreshPool_IsFalse()
    {
        var pool = Pool();

        Assert.That(pool.IsFull, Is.False);
    }

    // A full pool hands out exactly its slot count and keeps the last stub inside the block.
    [Test]
    public void TryRent_AFullPool_HandsOutNothingMoreAndStaysInsideItself()
    {
        var pool = Pool();

        var lastSlot = Address.Zero;
        int rented = 0;

        while (pool.TryRent(out var record, out var slot))
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pool.Contains(record), Is.True);
                Assert.That(pool.Contains(slot), Is.True);
            }

            lastSlot = slot;
            rented++;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rented, Is.EqualTo(0x6000 / CaptureLayout.RecordSize));
            Assert.That(pool.IsFull, Is.True);
            Assert.That(pool.Contains(lastSlot + (CodePatch.SlotSize - 1)), Is.True);

            Assert.That(pool.TryRent(out var record, out var slot), Is.False);
            Assert.That(record, Is.EqualTo(Address.Zero));
            Assert.That(slot, Is.EqualTo(Address.Zero));
        }
    }

    // The slot handed out last comes back as the next one, so an install that reached nothing spends no slot.
    [Test]
    public void Return_TheSlotHandedOutLast_GivesItBackAsTheNextOne()
    {
        var pool = Pool();

        var first = Rent(pool);
        var second = Rent(pool);

        pool.Return(second.Slot);

        var again = Rent(pool);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(second));
        }
    }

    // Any slot but the last is refused. A refusal costs the pool nothing.
    [Test]
    public void Return_AnySlotButTheLast_RefusesIt()
    {
        var pool = Pool();

        Assert.That(() => pool.Return(pool.Base), Throws.ArgumentException);

        var first = Rent(pool);
        var second = Rent(pool);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => pool.Return(first.Slot), Throws.ArgumentException);
            Assert.That(() => pool.Return(second.Slot + CodePatch.SlotSize), Throws.ArgumentException);
        }

        pool.Return(second.Slot);

        Assert.That(Rent(pool).Slot, Is.EqualTo(second.Slot));
    }

    // An entry a relative jump reaches on either side is in range.
    [Test]
    public void CanReach_AnEntryWithinARelativeJumpEitherSide_IsTrue()
    {
        var pool = Pool();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool.CanReach(pool.Base - 0x7FFEFFFE), Is.True);
            Assert.That(pool.CanReach(pool.Base + 0x7FFFFFFF), Is.True);
        }
    }

    // An entry 1 byte past the reach on either side is out of range, pinning the exclusive bounds.
    [Test]
    public void CanReach_AnEntryOneByteBeyondReach_IsFalse()
    {
        var pool = Pool();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool.CanReach(pool.Base - 0x7FFEFFFF), Is.False);
            Assert.That(pool.CanReach(pool.Base + 0x80000000), Is.False);
        }
    }

    // A 32-bit entry near a 32-bit base is in range, since the pool only measures the distance.
    [Test]
    public void CanReach_A32BitEntryNearA32BitBase_IsTrue()
    {
        var writer = new FakePatchWriter { NextBase = new(0x410000) };
        var pool = PatchPool.Create(writer, Address.Zero).Unwrap();

        Assert.That(pool.CanReach(new Address(0x400000)), Is.True);
    }

    // An entry across the top of 32-bit space is out of range, since the distance is below int.MinValue.
    [Test]
    public void CanReach_AnEntryAcrossTheTopOf32BitSpace_IsFalse()
    {
        var writer = new FakePatchWriter { NextBase = new(0x10000) };
        var pool = PatchPool.Create(writer, Address.Zero).Unwrap();

        Assert.That(pool.CanReach(new Address(0xFFFF0000)), Is.False);
    }

    // Address 0 is beyond a high base's reach but within a low base's, covering the 0 edge.
    [Test]
    public void CanReach_AddressZero_IsTrueOnlyFromALowBase()
    {
        var high = PatchPool.Create(new FakePatchWriter { NextBase = new(0x1_0000_0000) }, Address.Zero).Unwrap();
        var low = PatchPool.Create(new FakePatchWriter { NextBase = new(0x10000) }, Address.Zero).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(high.CanReach(Address.Zero), Is.False);
            Assert.That(low.CanReach(Address.Zero), Is.True);
        }
    }

    // The first and last byte of the reservation are contained, the bytes either side are not.
    [Test]
    public void Contains_TheFirstAndLastByte_IsTrue()
    {
        var pool = Pool();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool.Contains(pool.Base), Is.True);
            Assert.That(pool.Contains(pool.Base + 0xFFFF), Is.True);
            Assert.That(pool.Contains(pool.Base - 1), Is.False);
            Assert.That(pool.Contains(pool.Base + 0x10000), Is.False);
        }
    }

    // A base whose end wraps past the top still contains its last byte but not the wrapped end bound at 0.
    [Test]
    public void Contains_ABaseWhoseEndWrapsPastTheTop_ContainsItsLastByte()
    {
        var writer = new FakePatchWriter { NextBase = new(ulong.MaxValue - 0xFFFF) };
        var pool = PatchPool.Create(writer, Address.Zero).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pool.Contains(new Address(ulong.MaxValue)), Is.True);
            Assert.That(pool.Contains(pool.Base + 0x10000), Is.False);
        }
    }

    private static PatchPool Pool()
    {
        FakePatchWriter writer = new() { NextBase = new(0x1_0000_0000) };

        return PatchPool.Create(writer, Address.Zero).Unwrap();
    }

    // Asserts the rent succeeded before returning the pair, since a slot and its record mean nothing otherwise.
    private static (Address Record, Address Slot) Rent(PatchPool pool)
    {
        Assert.That(pool.TryRent(out var record, out var slot), Is.True);

        return (record, slot);
    }
}
