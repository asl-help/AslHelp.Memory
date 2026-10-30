using AslHelp.Memory;
using AslHelp.Memory.Patching;
using NUnit.Framework;
using System;

namespace AslHelp.Memory.Tests.Patching;

// Tests the edges of installing and restoring: the last slot, misaligned and adopted entries, the 32-bit restore and
// the failures the main fixture leaves out. Every write goes through a FakePatchWriter backed by a BufferMemory.
[TestFixture]
public class CodePatchInstallEdgeTests
{
    private const int PoolSize = 0x10000;

    private static readonly Address _base = new(0x1_0000_0000);
    private static readonly Address _entry = _base + 0x20000;

    private static readonly Address _base32 = new(0x400000);
    private static readonly Address _entry32 = new(0x420000);

    // `sub rsp, 0x28` and a `nop`, 5 bytes displaced whole.
    private static readonly byte[] _prologue = [0x48, 0x83, 0xEC, 0x28, 0x90];

    // `push ebp`, `mov ebp, esp` and `sub esp, 8`, the MSVC prologue that displaces 6 bytes.
    private static readonly byte[] _prologue32 = [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08];

    // The last slot of a pool sits inside the reservation and is redirected to like the first.
    [Test]
    public void Install_TheLastSlotOfAPool_RedirectsAndFillsThePool()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = CreatePlan(memory, _entry, CaptureLayout.None);
        var last = Address.Zero;

        while (pool.TryRent(out _, out var slot))
        {
            last = slot;
        }

        pool.Return(last);

        var patch = CodePatch.Install(memory, writer, pool, plan, out _).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Slot + CodePatch.SlotSize - pool.Base, Is.LessThanOrEqualTo(PoolSize));
            Assert.That(patch.Record + CaptureLayout.RecordSize - pool.Base, Is.LessThanOrEqualTo(0x6000));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(Jump(_entry, patch.Slot)));
            Assert.That(pool.IsFull, Is.True);
        }
    }

    // A 2nd install into the same pool takes the next slot and the next record.
    [Test]
    public void Install_TwoEntriesInOnePool_TakeConsecutiveSlotsAndRecords()
    {
        var second = _entry + 0x10;
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        memory.Write(second, _prologue).Unwrap();

        var first = CodePatch.Install(memory, writer, pool, CreatePlan(memory, _entry, CaptureLayout.None), out _).Unwrap();
        var next = CodePatch.Install(memory, writer, pool, CreatePlan(memory, second, CaptureLayout.None), out _).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(next.Slot, Is.EqualTo(first.Slot + CodePatch.SlotSize));
            Assert.That(next.Record, Is.EqualTo(first.Record + CaptureLayout.RecordSize));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(Jump(_entry, first.Slot)));
            Assert.That(ReadAt(memory, second, 5), Is.EqualTo(Jump(second, next.Slot)));
        }
    }

    // A thread in the 3 bytes before an entry 3 bytes into its word is outside the patch, though the word is written.
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Install_AThreadBeforeAnUnalignedEntry_Redirects(int offset)
    {
        var entry = _entry + 3;
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, entry, _prologue);
        var plan = CreatePlan(memory, entry, CaptureLayout.None);
        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(_entry + offset);
        writer.Suspension = suspension;

        var result = CodePatch.Install(memory, writer, pool, plan, out _);

        Assert.That(result.IsOk, Is.True);
    }

    // A thread on the last patched byte of an entry 3 bytes into its word is inside the patch.
    [Test]
    public void Install_AThreadOnTheLastByteOfAnUnalignedEntry_Refuses()
    {
        var entry = _entry + 3;
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, entry, _prologue);
        var plan = CreatePlan(memory, entry, CaptureLayout.None);
        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(entry + 4);
        writer.Suspension = suspension;

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error!.Message, Does.Contain("stands inside"));
            Assert.That(attempted, Is.Null);
        }
    }

    // A word that can't be read inside the suspension stops before the write, so the slot goes back.
    [Test]
    public void Install_AnUnreadableWordAtTheEntry_ReturnsTheSlotAndNoAttempt()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var gone = _entry + 0x1000;
        BufferMemory planned = new(gone, [.. _prologue, .. new byte[0x40]], MemoryLayout.X64);
        var plan = CreatePlan(planned, gone, CaptureLayout.None);
        var (_, slot) = NextSlot(pool);

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(writer.Written, Has.Count.EqualTo(2));
            Assert.That(attempted, Is.Null);
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(slot));
        }
    }

    // A successful redirect resumes the threads it suspended.
    [Test]
    public void Install_ASuccessfulRedirect_DisposesTheSuspension()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        FakeThreadSuspension suspension = new();
        writer.Suspension = suspension;

        CodePatch.Install(memory, writer, pool, CreatePlan(memory, _entry, CaptureLayout.None), out _).Unwrap();

        Assert.That(suspension.Disposed, Is.True);
    }

    // A redirect write that fails still resumes the threads it suspended.
    [Test]
    public void Install_ARedirectWriteThatFails_DisposesTheSuspension()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        FakeThreadSuspension suspension = new();
        writer.Suspension = suspension;
        writer.WriteError = FakePatchWriter.CreateError();
        writer.FailWriteAt = _entry;

        var result = CodePatch.Install(memory, writer, pool, CreatePlan(memory, _entry, CaptureLayout.None), out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(suspension.Disposed, Is.True);
        }
    }

    // A suspension that missed a thread still resumes the threads it did suspend.
    [Test]
    public void Install_AnIncompleteSuspension_DisposesIt()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        FakeThreadSuspension suspension = new() { IsComplete = false };
        writer.Suspension = suspension;

        var result = CodePatch.Install(memory, writer, pool, CreatePlan(memory, _entry, CaptureLayout.None), out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(suspension.Disposed, Is.True);
        }
    }

    // A plan whose stub fails to compose at install time gives the slot back before anything is written.
    [Test]
    public void Install_AStubThatFailsToCompose_ReturnsTheSlotAndWritesNothing()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, CaptureLayout.PayloadOffset, 3)]);
        PatchPlan plan = new(_entry, _prologue, layout, StubAssembler.X64);
        var (_, slot) = NextSlot(pool);

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(writer.Written, Is.Empty);
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(slot));
            Assert.That(attempted, Is.Null);
        }
    }

    // A suspend that fails during a restore comes back as it is and writes nothing.
    [Test]
    public void Restore_ASuspendThatFails_ReturnsTheErrorAndWritesNothing()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var patch = CodePatch.Install(memory, writer, pool, CreatePlan(memory, _entry, CaptureLayout.None), out _).Unwrap();
        var error = FakePatchWriter.CreateError();
        writer.SuspendError = error;

        var result = patch.Restore(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(writer.Written, Has.Count.EqualTo(3));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(Jump(_entry, patch.Slot)));
        }
    }

    // A 32-bit restore puts the 5 jump bytes back and leaves the 6th displaced byte as it stood.
    [Test]
    public void Restore_A32BitPatch_PutsTheDisplacedBytesBack()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X86, _base32, _entry32, _prologue32);
        var patch = CodePatch.Install(memory, writer, pool, CreatePlan(memory, _entry32, CaptureLayout.None), out _).Unwrap();

        patch.Restore(memory, writer).Unwrap();

        byte[] word = [.. _prologue32, 0x00, 0x00];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Written[^1].Address, Is.EqualTo(_entry32));
            Assert.That(writer.Written[^1].Bytes, Is.EqualTo(word));
            Assert.That(ReadAt(memory, _entry32, 6), Is.EqualTo(_prologue32));
        }
    }

    // An entry in the last word of the address space is restored without an overflow escaping.
    [Test]
    public void Restore_AnEntryInTheLastWordOfAddressSpace_PutsTheBytesBack()
    {
        var entry = new Address(ulong.MaxValue - 7);
        var (memory, writer, pool) = Target(MemoryLayout.X64, new(0xFFFF_FFFF_FFFE_0000), entry, _prologue);
        var patch = CodePatch.Install(memory, writer, pool, CreatePlan(memory, entry, CaptureLayout.None), out _).Unwrap();

        var result = patch.Restore(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(ReadAt(memory, entry, 5), Is.EqualTo(_prologue));
        }
    }

    // An adopted patch 3 bytes into its word gets its bytes spliced back at that offset.
    [Test]
    public void Restore_AnAdoptedPatchNotAtAWordStart_SplicesTheBytesBack()
    {
        var (memory, writer, patch) = Adopted(new(0x20000), 3);
        memory.Write(new Address(0x20000), [0xAA, 0xBB, 0xCC]).Unwrap();

        patch.Restore(memory, writer).Unwrap();

        byte[] word = [0xAA, 0xBB, 0xCC, .. _prologue];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Written[0].Address, Is.EqualTo(new Address(0x20000)));
            Assert.That(writer.Written[0].Bytes, Is.EqualTo(word));
        }
    }

    // An adopted patch whose jump crosses an 8-byte boundary can't be put back in 1 store, so it's refused.
    [TestCase(4)]
    [TestCase(7)]
    public void Restore_AnAdoptedPatchAcrossAWord_FailsWithoutWriting(int offset)
    {
        var (memory, writer, patch) = Adopted(new(0x20000), offset);

        var result = patch.Restore(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain(patch.Entry.ToString()));
            Assert.That(writer.Written, Is.Empty);
            Assert.That(writer.Suspensions, Is.Zero);
        }
    }

    // Maps 1 array over the pool's block and the entry, with the prologue at the entry and a writer backed by it.
    private static (BufferMemory Memory, FakePatchWriter Writer, PatchPool Pool) Target(
        MemoryLayout layout,
        Address @base,
        Address entry,
        byte[] prologue)
    {
        var low = entry < @base ? entry : @base;
        long size = Math.Max(@base - low + PoolSize, entry - low + 0x40);

        BufferMemory memory = new(low, new byte[size], layout);
        memory.Write(entry, prologue).Unwrap();

        FakePatchWriter writer = new() { NextBase = @base, Backing = memory };
        var pool = PatchPool.Create(writer, entry).Unwrap();

        return (memory, writer, pool);
    }

    // Composes a page for an entry `offset` bytes past `word`, writes the jump to it and adopts what it finds.
    private static (BufferMemory Memory, FakePatchWriter Writer, CodePatch Patch) Adopted(Address word, int offset)
    {
        var entry = word + offset;
        var slot = word + 0x40;
        var record = word + 0x200;
        byte[] bytes = new byte[0x200 + CaptureLayout.RecordSize];

        byte[] page = CodePatch.Assemble(entry, slot, record, _prologue, CaptureLayout.None, StubAssembler.X64).Unwrap();
        page.CopyTo(bytes, 0x40);
        Jump(entry, slot).CopyTo(bytes, offset);

        BufferMemory memory = new(word, bytes, MemoryLayout.X64);
        FakePatchWriter writer = new() { Backing = memory };

        return (memory, writer, CodePatch.Adopt(memory, entry).Unwrap());
    }

    private static PatchPlan CreatePlan(IMemoryReader memory, Address entry, CaptureLayout layout)
    {
        return CodePatch.CreatePlan(memory, entry, layout).Unwrap();
    }

    // Peeks at the slot the next install rents by renting it and giving it straight back.
    private static (Address Record, Address Slot) NextSlot(PatchPool pool)
    {
        Assert.That(pool.TryRent(out var record, out var slot), Is.True);
        pool.Return(slot);

        return (record, slot);
    }

    private static byte[] Jump(Address entry, Address slot)
    {
        int displacement = (int)(slot + 0x30 - (entry + 5));

        return [0xE9, .. BitConverter.GetBytes(displacement)];
    }

    private static byte[] ReadAt(BufferMemory memory, Address address, int length)
    {
        byte[] bytes = new byte[length];
        memory.Read(address, bytes).Unwrap();

        return bytes;
    }
}
