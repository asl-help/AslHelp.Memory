using AslHelp.Memory;
using AslHelp.Memory.Patching;
using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using static AslHelp.Memory.Tests.Patching.SessionTargets;

namespace AslHelp.Memory.Tests.Patching;

// Tests the session's refusals, retries and pool bounds that the install and dispose fixtures leave out. Every write
// goes through a FakePatchWriter backed by a BufferMemory, so no process is written and no thread is suspended.
[TestFixture]
public class PatchSessionEdgeTests
{
    // The 1st stub of a fresh pool sits right after its 6 record pages.
    private static readonly Address _firstSlot = Base + 0x6000;

    // An entry outside every mapped byte is refused by the plan before a pool is reserved.
    [Test]
    public void Install_AnUnreadableEntry_RefusesWithoutReserving()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);

        var result = session.Install(Entry + 0x10_0000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(writer.Reserved, Is.Empty);
            Assert.That(writer.Suspensions, Is.Zero);
            Assert.That(session.Patches, Is.Empty);
        }
    }

    // A jump at the entry into a stub that isn't ours is not adopted and is refused as position dependent.
    [Test]
    public void Install_AForeignJumpAtTheEntry_RefusesWithoutReserving()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        byte[] foreign = Jump(Entry, Base + 0x100);
        memory.Write(Entry, foreign).Unwrap();
        using PatchSession session = new(memory, writer);

        var result = session.Install(Entry);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(writer.Reserved, Is.Empty);
            Assert.That(writer.Written, Is.Empty);
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(foreign));
        }
    }

    // A 2nd install with a capturing layout returns the standing patch, which still only counts.
    [Test]
    public void Install_TheSameEntryWithALayout_ReturnsTheStandingPatch()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);
        var first = session.Install(Entry).Unwrap();
        int written = writer.Written.Count;
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, CaptureLayout.PayloadAt, 8)]);

        var second = session.Install(Entry, layout).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(second.Captures, Is.False);
            Assert.That(writer.Written, Has.Count.EqualTo(written));
        }
    }

    // A retry after a redirect write that missed the target takes the next slot, since the 1st one stays spent.
    [Test]
    public void Install_ARetryAfterAFailedRedirect_TakesTheNextSlot()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        writer.WriteError = FakePatchWriter.CreateError();
        writer.FailWriteAt = Entry;
        PatchSession session = new(memory, writer);
        Assert.That(session.Install(Entry).IsErr, Is.True);

        writer.WriteError = null;
        var patch = session.Install(Entry).Unwrap();
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.WasAdopted, Is.False);
            Assert.That(patch.Slot, Is.EqualTo(_firstSlot + CodePatch.SlotSize));
            Assert.That(writer.Reserved, Has.Count.EqualTo(1));
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.EquivalentTo([Base]));
        }
    }

    // A retry after a redirect write that failed but reached the target takes back the session's own patch. That patch
    // still captures and its entry is put back once.
    [Test]
    public void Install_ARetryAfterARedirectThatReachedTheTarget_TakesBackItsOwnPatch()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, CaptureLayout.PayloadAt, 8)]);
        writer.WriteError = FakePatchWriter.CreateError();
        writer.FailWriteAt = Entry;
        PatchSession session = new(memory, writer);
        Assert.That(session.Install(Entry, layout).IsErr, Is.True);

        writer.WriteError = null;
        memory.Write(Entry, Jump(Entry, _firstSlot)).Unwrap();
        var patch = session.Install(Entry, layout).Unwrap();
        var call = patch.TryReadCall(memory, new byte[8]);
        int written = writer.Written.Count;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.WasAdopted, Is.False);
            Assert.That(patch.Captures, Is.True);
            Assert.That(patch.Slot, Is.EqualTo(_firstSlot));
            Assert.That(call.IsOk, Is.True);
            Assert.That(writer.Reserved, Has.Count.EqualTo(1));
            Assert.That(writer.Written, Has.Count.EqualTo(written + 1));
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.EquivalentTo([Base]));
        }
    }

    // Installs racing at 1 entry from many threads get 1 patch out of 1 reservation.
    [Test]
    public void Install_ConcurrentCallsAtOneEntry_InstallOnce()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);
        ConcurrentBag<CodePatch> patches = [];

        Parallel.For(0, 16, _ => patches.Add(session.Install(Entry).Unwrap()));

        Assert.That(patches.TryPeek(out var one), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patches, Has.Count.EqualTo(16));
            Assert.That(patches, Has.All.SameAs(one));
            Assert.That(writer.Reserved, Has.Count.EqualTo(1));
            Assert.That(session.Patches, Is.EquivalentTo([one]));
        }
    }

    // A 32-bit adopt follows the jump to the 1st session's slot and its unload restores without a release check.
    [Test]
    public void Install_A32BitEntryAnotherSessionPatched_AdoptsAndRestoresIt()
    {
        var (memory, writer) = Target(MemoryLayout.X86, Base32, Entry32, Prologue32);
        PatchSession first = new(memory, writer);
        var installed = first.Install(Entry32).Unwrap();
        PatchSession second = new(memory, writer);

        var adopted = second.Install(Entry32).Unwrap();
        int before = writer.Suspensions;
        second.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(adopted.WasAdopted, Is.True);
            Assert.That(adopted.Slot, Is.EqualTo(installed.Slot));
            Assert.That(adopted.Record, Is.EqualTo(installed.Record));
            Assert.That(ReadAt(memory, Entry32, 6), Is.EqualTo(Prologue32));
            Assert.That(writer.Suspensions - before, Is.EqualTo(2));
            Assert.That(writer.Released, Is.Empty);
        }
    }

    // A failed restore of 1 of 2 patches in 1 pool keeps that pool, though the other entry is put back.
    [Test]
    public void Dispose_OneFailedRestoreOfTwoInOnePool_KeepsThePool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue, 0x140);
        var other = Entry + 0x100;
        memory.Write(other, Prologue).Unwrap();
        EntryReadFailure failing = new(memory, Entry);
        var (logger, sink) = Logged();
        PatchSession session = new(failing, writer, logger);
        session.Install(Entry).Unwrap();
        session.Install(other).Unwrap();

        failing.Fail = true;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, other, 5), Is.EqualTo(Prologue));
            Assert.That(ReadAt(memory, Entry, 1), Is.EqualTo(new byte[] { 0xE9 }));
            Assert.That(writer.Released, Is.Empty);
            Assert.That(Warnings(sink, "still jumps into it"), Is.EqualTo(1));
        }

        logger.Dispose();
    }

    // An adopted patch left standing points into another session's pool, so this session still frees its own.
    [Test]
    public void Dispose_AnAdoptedPatchThatCannotBePutBack_ReleasesTheSessionsOwnPool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue, 0x140);
        var other = Entry + 0x100;
        memory.Write(other, Prologue).Unwrap();
        PatchSession first = new(memory, writer);
        first.Install(Entry).Unwrap();

        var ownBase = Base + PoolSize;
        FakePatchWriter ownWriter = new() { NextBase = ownBase, Backing = memory };
        EntryReadFailure failing = new(memory, Entry);
        var (logger, sink) = Logged();
        PatchSession second = new(failing, ownWriter, logger);
        Assert.That(second.Install(Entry).Unwrap().WasAdopted, Is.True);
        second.Install(other).Unwrap();

        failing.Fail = true;
        second.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, other, 5), Is.EqualTo(Prologue));
            Assert.That(ownWriter.Released, Is.EquivalentTo([ownBase]));
            Assert.That(Warnings(sink, "could not be put back"), Is.EqualTo(1));
            Assert.That(Warnings(sink, "still jumps into it"), Is.Zero);
        }

        logger.Dispose();
    }

    // An unload suspension that fails costs only the batch, so every entry is still put back and the pool freed.
    [Test]
    public void Dispose_AnUnloadSuspendThatFails_StillRestoresAndReleases()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        SuspendFailure failing = new(writer);
        PatchSession session = new(memory, failing);
        session.Install(Entry).Unwrap();

        failing.FailAt = failing.Calls + 1;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failing.Calls, Is.EqualTo(writer.Suspensions + 1));
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.EquivalentTo([Base]));
        }
    }

    // 2 patches cost 1 unload suspension, 1 per restore and 1 to check the pool.
    [Test]
    public void Dispose_TwoPatches_SuspendsFourTimes()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue, 0x140);
        var other = Entry + 0x100;
        memory.Write(other, Prologue).Unwrap();
        PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();
        session.Install(other).Unwrap();
        int before = writer.Suspensions;

        session.Dispose();

        Assert.That(writer.Suspensions - before, Is.EqualTo(4));
    }

    // A thread stopped on the pool's last byte is inside it and keeps it.
    [Test]
    public void Dispose_AThreadOnThePoolsLastByte_KeepsIt()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();

        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(Base + (PoolSize - 1));
        writer.Suspension = suspension;
        session.Dispose();

        Assert.That(writer.Released, Is.Empty);
    }

    // A thread stopped 1 byte past the pool is outside it, so the pool is freed.
    [Test]
    public void Dispose_AThreadOnePastThePool_ReleasesIt()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();

        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(Base + PoolSize);
        writer.Suspension = suspension;
        session.Dispose();

        Assert.That(writer.Released, Is.EquivalentTo([Base]));
    }

    // A pool whose end wraps past the top keeps a thread on its last byte inside and one at address 0 outside.
    [TestCase(ulong.MaxValue, false)]
    [TestCase(0UL, true)]
    public void Dispose_APoolAtTheTopOfAddressSpace_ChecksThreadsAcrossTheWrap(ulong position, bool released)
    {
        var top = new Address(0xFFFF_FFFF_FFFF_0000);
        var entry = new Address(0xFFFF_FFFF_FFFE_0000);
        var (memory, writer) = Target(MemoryLayout.X64, top, entry, Prologue);
        PatchSession session = new(memory, writer);
        var patch = session.Install(entry).Unwrap();

        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(new Address(position));
        writer.Suspension = suspension;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Slot, Is.EqualTo(top + 0x6000));
            Assert.That(ReadAt(memory, entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, released ? Is.EquivalentTo([top]) : Is.Empty);
        }
    }

    // Fails the suspension numbered `FailAt` and passes every other call to the inner writer, because the fake checks
    // its error before any hook runs and can't fail only the 1st call of a dispose.
    private sealed class SuspendFailure(
        FakePatchWriter inner) : IPatchWriter
    {
        public int Calls { get; private set; }

        public int FailAt { get; set; } = -1;

        public Result<Address> Reserve(Address anchor, int size)
        {
            return inner.Reserve(anchor, size);
        }

        public Result Protect(Address address, int size, MemoryProtection protection)
        {
            return inner.Protect(address, size, protection);
        }

        public Result WriteCode(Address address, ReadOnlySpan<byte> buffer)
        {
            return inner.WriteCode(address, buffer);
        }

        public Result Release(Address reservation)
        {
            return inner.Release(reservation);
        }

        public Result<IThreadSuspension> SuspendThreads()
        {
            if (++Calls == FailAt)
            {
                return FakePatchWriter.CreateError();
            }

            return inner.SuspendThreads();
        }
    }
}
