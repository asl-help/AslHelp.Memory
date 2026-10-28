using AslHelp.Memory;
using AslHelp.Memory.Patching;
using NUnit.Framework;
using System;

namespace AslHelp.Memory.Tests.Patching;

// Tests installing and restoring against a BufferMemory that spans the pool's block and an entry past it. The writes
// go through a FakePatchWriter backed by that memory, so no process is written and no thread is suspended.
[TestFixture]
public class CodePatchInstallTests
{
    private const int PoolSize = 0x10000;

    private static readonly Address _base = new(0x1_0000_0000);
    private static readonly Address _entry = _base + 0x20000;

    private static readonly Address _base32 = new(0x400000);
    private static readonly Address _entry32 = new(0x420000);

    // `sub rsp, 0x28` and a `nop`, 5 bytes displaced whole.
    private static readonly byte[] _prologue = [0x48, 0x83, 0xEC, 0x28, 0x90];

    // `sub rsp, 0x28` and `mov rdi, rcx`, 7 bytes displaced, 2 more than the jump replaces.
    private static readonly byte[] _wide = [0x48, 0x83, 0xEC, 0x28, 0x48, 0x8B, 0xF9];

    // `push ebp`, `mov ebp, esp` and `sub esp, 8`, the MSVC prologue that displaces 6 bytes.
    private static readonly byte[] _prologue32 = [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08];

    // An install writes the record clear, then the stub, then 1 word at the entry with the jump in it.
    [Test]
    public void Install_APlannedEntry_WritesTheStubThenRedirectsTheEntryInOneWord()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        var (record, slot) = NextSlot(pool);

        var patch = CodePatch.Install(memory, writer, pool, plan, out var attempted).Unwrap();

        byte[] stub = CodePatch.Compose(_entry, slot, record, plan.Original, CaptureLayout.None, StubAssembler.X64).Unwrap();
        byte[] word = [.. Jump(_entry, slot), 0x00, 0x00, 0x00];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Written, Has.Count.EqualTo(3));
            Assert.That(writer.Written[0].Address, Is.EqualTo(record));
            Assert.That(writer.Written[0].Bytes, Is.EqualTo(new byte[CaptureLayout.RecordSize]));
            Assert.That(writer.Written[1].Address, Is.EqualTo(slot));
            Assert.That(writer.Written[1].Bytes, Is.EqualTo(stub));
            Assert.That(writer.Written[2].Address, Is.EqualTo(_entry));
            Assert.That(writer.Written[2].Bytes, Is.EqualTo(word));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(Jump(_entry, slot)));
            Assert.That(patch.Entry, Is.EqualTo(_entry));
            Assert.That(patch.Slot, Is.EqualTo(slot));
            Assert.That(patch.Record, Is.EqualTo(record));
            Assert.That(patch.WasAdopted, Is.False);
            Assert.That(patch.Captures, Is.False);
            Assert.That(attempted, Is.Null);
            Assert.That(writer.Suspensions, Is.EqualTo(1));
        }
    }

    // A layout with something to copy yields a patch that reports it captures and a stub composed with that layout.
    [Test]
    public void Install_ACapturingLayout_ReportsThatItCaptures()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, CaptureLayout.PayloadAt, 8)]);
        var plan = Plan(memory, _entry, layout);
        var (record, slot) = NextSlot(pool);

        var patch = CodePatch.Install(memory, writer, pool, plan, out _).Unwrap();

        byte[] stub = CodePatch.Compose(_entry, slot, record, plan.Original, layout, StubAssembler.X64).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Captures, Is.True);
            Assert.That(writer.Written[1].Bytes, Is.EqualTo(stub));
        }
    }

    // An entry 3 bytes into its word is written as the whole word, the 3 bytes before it written back unchanged.
    [Test]
    public void Install_AnEntryNotAtAWordStart_SplicesTheJumpIntoItsAlignedWord()
    {
        var entry = _entry + 3;
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, entry, _prologue);
        memory.Write(_entry, [0xAA, 0xBB, 0xCC]).Unwrap();
        var plan = Plan(memory, entry, CaptureLayout.None);
        var (_, slot) = NextSlot(pool);

        CodePatch.Install(memory, writer, pool, plan, out _).Unwrap();

        byte[] word = [0xAA, 0xBB, 0xCC, .. Jump(entry, slot)];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Written[2].Address, Is.EqualTo(_entry));
            Assert.That(writer.Written[2].Bytes, Is.EqualTo(word));
        }
    }

    // A 32-bit target gets the X86 stub and the same 5-byte jump, leaving the 6th displaced byte as it was.
    [Test]
    public void Install_A32BitTarget_RedirectsWithTheSameJump()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X86, _base32, _entry32, _prologue32);
        var plan = Plan(memory, _entry32, CaptureLayout.None);
        var (record, slot) = NextSlot(pool);

        CodePatch.Install(memory, writer, pool, plan, out _).Unwrap();

        byte[] stub = CodePatch.Compose(_entry32, slot, record, plan.Original, CaptureLayout.None, StubAssembler.X86).Unwrap();
        byte[] word = [.. Jump(_entry32, slot), 0x08, 0x00, 0x00];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Original, Is.EqualTo(_prologue32));
            Assert.That(writer.Written[1].Bytes, Is.EqualTo(stub));
            Assert.That(writer.Written[2].Address, Is.EqualTo(_entry32));
            Assert.That(writer.Written[2].Bytes, Is.EqualTo(word));
        }
    }

    // An entry at address 0 aligns to itself and jumps forward to a pool above it.
    [Test]
    public void Install_AnEntryAtAddressZero_Redirects()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, new(0x10000), Address.Zero, _prologue);
        var plan = Plan(memory, Address.Zero, CaptureLayout.None);
        var (_, slot) = NextSlot(pool);

        var result = CodePatch.Install(memory, writer, pool, plan, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(writer.Written[2].Address, Is.EqualTo(Address.Zero));
            Assert.That(ReadAt(memory, Address.Zero, 5), Is.EqualTo(Jump(Address.Zero, slot)));
        }
    }

    // An entry in the last word of the address space resumes past the top without an overflow escaping.
    [Test]
    public void Install_AnEntryInTheLastWordOfAddressSpace_Redirects()
    {
        var entry = new Address(ulong.MaxValue - 7);
        var (memory, writer, pool) = Target(MemoryLayout.X64, new(0xFFFF_FFFF_FFFE_0000), entry, _prologue);
        var plan = Plan(memory, entry, CaptureLayout.None);
        var (_, slot) = NextSlot(pool);

        var result = CodePatch.Install(memory, writer, pool, plan, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(writer.Written[2].Address, Is.EqualTo(entry));
            Assert.That(ReadAt(memory, entry, 5), Is.EqualTo(Jump(entry, slot)));
        }
    }

    // A pool 2 GB from the entry is refused before a slot is rented or a thread suspended.
    [Test]
    public void Install_APoolOutOfReach_RefusesBeforeRentingASlot()
    {
        var (_, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var entry = pool.Base + 0x8000_0000;
        BufferMemory far = new(entry, [.. _prologue, .. new byte[0x40]], MemoryLayout.X64);
        var plan = Plan(far, entry, CaptureLayout.None);

        var result = CodePatch.Install(far, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("reach"));
            Assert.That(result.Error!.Message, Does.Contain(pool.Base.ToString()));
            Assert.That(result.Error!.Message, Does.Contain(entry.ToString()));
            Assert.That(writer.Written, Is.Empty);
            Assert.That(writer.Suspensions, Is.Zero);
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(pool.Base + 0x6000));
            Assert.That(attempted, Is.Null);
        }
    }

    // A pool with every slot rented refuses and writes nothing.
    [Test]
    public void Install_AFullPool_RefusesAndWritesNothing()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);

        while (pool.TryRent(out _, out _))
        {
        }

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("no slot left"));
            Assert.That(writer.Written, Is.Empty);
            Assert.That(pool.IsFull, Is.True);
            Assert.That(attempted, Is.Null);
        }
    }

    // A thread stopped inside the 5 patched bytes refuses the redirect and the slot goes back to the pool.
    [TestCase(1)]
    [TestCase(4)]
    public void Install_AThreadInsideThePatchedBytes_RefusesAndReturnsTheSlot(int offset)
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        var (record, slot) = NextSlot(pool);
        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(_entry + offset);
        writer.Suspension = suspension;

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("thread"));
            Assert.That(writer.Written, Has.Count.EqualTo(2));
            Assert.That(writer.Written[0].Address, Is.EqualTo(record));
            Assert.That(writer.Written[1].Address, Is.EqualTo(slot));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(_prologue));
            Assert.That(attempted, Is.Null);
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(slot));
            Assert.That(suspension.Disposed, Is.True);
        }
    }

    // A thread at the entry's first byte or just past the 5 patched bytes is outside them.
    [TestCase(0)]
    [TestCase(5)]
    public void Install_AThreadAtTheEntryOrJustPastThePatch_Redirects(int offset)
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(_entry + offset);
        writer.Suspension = suspension;

        var result = CodePatch.Install(memory, writer, pool, plan, out _);

        Assert.That(result.IsOk, Is.True);
    }

    // A suspension that missed a thread refuses, since a missing answer is not a safe one.
    [Test]
    public void Install_AnIncompleteSuspension_RefusesAndReturnsTheSlot()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        var (_, slot) = NextSlot(pool);
        writer.Suspension = new FakeThreadSuspension { IsComplete = false };

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("every thread"));
            Assert.That(writer.Written, Has.Count.EqualTo(2));
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(slot));
            Assert.That(attempted, Is.Null);
        }
    }

    // A thread inside is reported ahead of a suspension that missed a thread.
    [Test]
    public void Install_AThreadInsideAndAnIncompleteSuspension_ReportsTheThreadFirst()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        FakeThreadSuspension suspension = new() { IsComplete = false };
        suspension.Positions.Add(_entry + 2);
        writer.Suspension = suspension;

        var result = CodePatch.Install(memory, writer, pool, plan, out _);

        Assert.That(result.Error!.Message, Does.Contain("stands inside"));
    }

    // A suspend that fails comes back as it is and the slot goes back to the pool.
    [Test]
    public void Install_ASuspendThatFails_ReturnsTheErrorAndReturnsTheSlot()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        var (_, slot) = NextSlot(pool);
        var error = FakePatchWriter.CreateError();
        writer.SuspendError = error;

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(writer.Written, Has.Count.EqualTo(2));
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(slot));
            Assert.That(attempted, Is.Null);
        }
    }

    // A record clear that fails stops before the stub and the slot stays spent.
    [Test]
    public void Install_ARecordClearThatFails_KeepsTheSlotAndWritesNoStub()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        var (record, slot) = NextSlot(pool);
        var error = FakePatchWriter.CreateError();
        writer.WriteError = error;
        writer.FailWriteAt = record;

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(writer.Written, Has.Count.EqualTo(1));
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(slot + CodePatch.SlotSize));
            Assert.That(attempted, Is.Null);
        }
    }

    // A stub write that fails stops before any thread is suspended and the slot stays spent.
    [Test]
    public void Install_AStubWriteThatFails_KeepsTheSlotAndLeavesTheEntryAlone()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        var (_, slot) = NextSlot(pool);
        var error = FakePatchWriter.CreateError();
        writer.WriteError = error;
        writer.FailWriteAt = slot;

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(writer.Suspensions, Is.Zero);
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(_prologue));
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(slot + CodePatch.SlotSize));
            Assert.That(attempted, Is.Null);
        }
    }

    // A redirect write that fails may still have reached the target, so the patch comes back as attempted.
    [Test]
    public void Install_ARedirectWriteThatFails_ReportsTheAttemptedPatch()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, CaptureLayout.PayloadAt, 8)]);
        var plan = Plan(memory, _entry, layout);
        var (record, slot) = NextSlot(pool);
        var error = FakePatchWriter.CreateError();
        writer.WriteError = error;
        writer.FailWriteAt = _entry;

        var result = CodePatch.Install(memory, writer, pool, plan, out var attempted);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(attempted, Is.Not.Null);
            Assert.That(attempted!.Entry, Is.EqualTo(_entry));
            Assert.That(attempted.Slot, Is.EqualTo(slot));
            Assert.That(attempted.Record, Is.EqualTo(record));
            Assert.That(attempted.Captures, Is.True);
            Assert.That(NextSlot(pool).Slot, Is.EqualTo(slot + CodePatch.SlotSize));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(_prologue));
        }
    }

    // A returned slot gets its record cleared before the stub, so a sequence left odd can't make every read the
    // half-written one.
    [Test]
    public void Install_AReturnedSlot_IsClearedBeforeItsStubIsWritten()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        var (record, slot) = NextSlot(pool);
        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(_entry + 1);
        writer.Suspension = suspension;
        Assert.That(CodePatch.Install(memory, writer, pool, plan, out _).IsErr, Is.True);

        byte[] dirty = new byte[CaptureLayout.RecordSize];
        dirty.AsSpan().Fill(0xFF);
        memory.Write(record, dirty).Unwrap();
        writer.Suspension = null;
        writer.Written.Clear();

        var patch = CodePatch.Install(memory, writer, pool, plan, out _).Unwrap();

        Address[] order = [record, slot, _entry];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Slot, Is.EqualTo(slot));
            Assert.That(writer.Written.ConvertAll(write => write.Address), Is.EqualTo(order));
            Assert.That(ReadAt(memory, record, CaptureLayout.RecordSize), Is.EqualTo(new byte[CaptureLayout.RecordSize]));
        }
    }

    // The word around the entry is read after the threads stop, so its 3 tail bytes go back as they are then.
    [Test]
    public void Install_TheAlignedWord_IsReadAfterTheThreadsStop()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        writer.OnSuspend = () => memory.Write(_entry + 6, [0xCC]).Unwrap();

        CodePatch.Install(memory, writer, pool, plan, out _).Unwrap();

        Assert.That(writer.Written[2].Bytes[6], Is.EqualTo(0xCC));
    }

    // A restore writes 1 word with the displaced bytes back in it and leaves the stub alone.
    [Test]
    public void Restore_AnInstalledPatch_WritesTheDisplacedBytesBackInOneWord()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var patch = CodePatch.Install(memory, writer, pool, Plan(memory, _entry, CaptureLayout.None), out _).Unwrap();
        byte[] stub = ReadAt(memory, patch.Slot, 0x40);

        var result = patch.Restore(memory, writer);

        byte[] word = [.. _prologue, 0x00, 0x00, 0x00];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(writer.Written, Has.Count.EqualTo(4));
            Assert.That(writer.Written[3].Address, Is.EqualTo(_entry));
            Assert.That(writer.Written[3].Bytes, Is.EqualTo(word));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(_prologue));
            Assert.That(writer.Suspensions, Is.EqualTo(2));
            Assert.That(ReadAt(memory, patch.Slot, 0x40), Is.EqualTo(stub));
        }
    }

    // A restore writes back only the 5 bytes the jump replaced, even when the patch displaced more.
    [Test]
    public void Restore_APatchThatDisplacedMoreThanTheJump_WritesBackOnlyTheJumpBytes()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _wide);
        var patch = CodePatch.Install(memory, writer, pool, Plan(memory, _entry, CaptureLayout.None), out _).Unwrap();
        memory.Write(_entry + 5, [0xAA, 0xBB, 0xCC]).Unwrap();

        patch.Restore(memory, writer).Unwrap();

        byte[] word = [0x48, 0x83, 0xEC, 0x28, 0x48, 0xAA, 0xBB, 0xCC];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Original, Has.Length.EqualTo(7));
            Assert.That(writer.Written[3].Bytes, Is.EqualTo(word));
        }
    }

    // A patch whose redirect failed can still be restored, which puts back a jump that may stand at the entry.
    [Test]
    public void Restore_AnAttemptedPatch_PutsTheEntryBack()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var plan = Plan(memory, _entry, CaptureLayout.None);
        writer.WriteError = FakePatchWriter.CreateError();
        writer.FailWriteAt = _entry;
        Assert.That(CodePatch.Install(memory, writer, pool, plan, out var attempted).IsErr, Is.True);
        writer.FailWriteAt = null;
        writer.WriteError = null;

        var result = attempted!.Restore(memory, writer);

        byte[] word = [.. _prologue, 0x00, 0x00, 0x00];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(writer.Written[^1].Address, Is.EqualTo(_entry));
            Assert.That(writer.Written[^1].Bytes, Is.EqualTo(word));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(_prologue));
        }
    }

    // A thread stopped inside the jump refuses the restore and the jump stays.
    [Test]
    public void Restore_AThreadInsideTheJump_Refuses()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var patch = CodePatch.Install(memory, writer, pool, Plan(memory, _entry, CaptureLayout.None), out _).Unwrap();
        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(_entry + 2);
        writer.Suspension = suspension;

        var result = patch.Restore(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("thread"));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(Jump(_entry, patch.Slot)));
        }
    }

    // A suspension that missed a thread refuses the restore and writes nothing.
    [Test]
    public void Restore_AnIncompleteSuspension_Refuses()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var patch = CodePatch.Install(memory, writer, pool, Plan(memory, _entry, CaptureLayout.None), out _).Unwrap();
        writer.Suspension = new FakeThreadSuspension { IsComplete = false };

        var result = patch.Restore(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("every thread"));
            Assert.That(writer.Written, Has.Count.EqualTo(3));
        }
    }

    // An entry write that fails during a restore comes back as the writer's own error.
    [Test]
    public void Restore_AnEntryWriteThatFails_ReturnsTheWriterError()
    {
        var (memory, writer, pool) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var patch = CodePatch.Install(memory, writer, pool, Plan(memory, _entry, CaptureLayout.None), out _).Unwrap();
        var error = FakePatchWriter.CreateError();
        writer.WriteError = error;
        writer.FailWriteAt = _entry;

        var result = patch.Restore(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(ReadAt(memory, _entry, 5), Is.EqualTo(Jump(_entry, patch.Slot)));
        }
    }

    // An entry whose word can't be read is not restored, since its tail bytes can't be spliced back.
    [Test]
    public void Restore_AnUnreadableWord_ReturnsTheReadError()
    {
        var (memory, writer, _) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        var outside = _entry + 0x1000;
        CodePatch patch = new(outside, _base + 0x6000, _base, _prologue, adopted: true, captures: false);

        var result = patch.Restore(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain(outside.ToString()));
            Assert.That(writer.Written, Is.Empty);
        }
    }

    // A null memory or writer is refused before anything is suspended.
    [Test]
    public void Restore_ANullMemoryOrWriter_Throws()
    {
        var (memory, writer, _) = Target(MemoryLayout.X64, _base, _entry, _prologue);
        CodePatch patch = new(_entry, _base + 0x6000, _base, _prologue, adopted: true, captures: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => patch.Restore(null!, writer), Throws.ArgumentNullException);
            Assert.That(() => patch.Restore(memory, null!), Throws.ArgumentNullException);
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

    private static PatchPlan Plan(IMemoryReader memory, Address entry, CaptureLayout layout)
    {
        return CodePatch.Plan(memory, entry, layout).Unwrap();
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
