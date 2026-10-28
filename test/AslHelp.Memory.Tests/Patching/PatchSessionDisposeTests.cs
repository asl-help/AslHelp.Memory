using AslHelp.Memory;
using AslHelp.Memory.Patching;
using NUnit.Framework;
using static AslHelp.Memory.Tests.Patching.SessionTargets;

namespace AslHelp.Memory.Tests.Patching;

// Tests what disposing a session leaves in the target. Every write goes through a FakePatchWriter backed by a
// BufferMemory. Every suspension is a fake, so no process is written and no thread is suspended.
[TestFixture]
public class PatchSessionDisposeTests
{
    // An entry put back reads its prologue again and its pool is released.
    [Test]
    public void Dispose_AnInstalledPatch_PutsTheEntryBackAndReleasesThePool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();

        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Written[^1].Address, Is.EqualTo(Entry));
            Assert.That(writer.Written[^1].Bytes, Has.Length.EqualTo(8));
            Assert.That(writer.Released, Is.EquivalentTo([Base]));
            Assert.That(session.Patches, Is.Empty);
        }
    }

    // A 1-patch session suspends once for the unload, once inside the restore and once to check the pool.
    [Test]
    public void Dispose_OnePatch_SuspendsThreeTimes()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();
        int before = writer.Suspensions;

        session.Dispose();

        Assert.That(writer.Suspensions - before, Is.EqualTo(3));
    }

    // 2 patches in 1 pool both read their prologue again and the pool is released once.
    [Test]
    public void Dispose_TwoPatchesInOnePool_RestoresBothAndReleasesOnce()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue, 0x140);
        var other = Entry + 0x100;
        memory.Write(other, Prologue).Unwrap();
        PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();
        session.Install(other).Unwrap();

        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(ReadAt(memory, other, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.EquivalentTo([Base]));
        }
    }

    // A 32-bit restore puts the 5 jump bytes back and leaves the 6th displaced byte as it stood.
    [Test]
    public void Dispose_A32BitPatch_PutsBackOnlyTheJumpBytes()
    {
        var (memory, writer) = Target(MemoryLayout.X86, Base32, Entry32, Prologue32);
        PatchSession session = new(memory, writer);
        session.Install(Entry32).Unwrap();

        session.Dispose();

        Assert.That(ReadAt(memory, Entry32, 6), Is.EqualTo(Prologue32));
    }

    // An adopted patch gets its entry put back, but its pool belongs to whoever wrote it and stays.
    [Test]
    public void Dispose_AnAdoptedPatch_PutsTheEntryBackButKeepsThePool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession first = new(memory, writer);
        first.Install(Entry).Unwrap();
        PatchSession second = new(memory, writer);
        second.Install(Entry).Unwrap();

        second.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.Empty);
        }
    }

    // A pool freed while its entry still jumps into it crashes the target, so an entry that can't be put back keeps it.
    [Test]
    public void Dispose_AnEntryThatCannotBePutBack_KeepsItsPool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        EntryReadFailure failing = new(memory, Entry);
        var (logger, sink) = Logged();
        PatchSession session = new(failing, writer, logger);
        session.Install(Entry).Unwrap();

        failing.Fail = true;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry, 1), Is.EqualTo(new byte[] { 0xE9 }));
            Assert.That(writer.Released, Is.Empty);
            Assert.That(Warnings(sink, "still jumps into it"), Is.EqualTo(1));
        }

        logger.Dispose();
    }

    // A restore write that fails keeps the jump and the pool with a warning for each.
    [Test]
    public void Dispose_ARestoreWriteThatFails_KeepsItsPool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        var (logger, sink) = Logged();
        PatchSession session = new(memory, writer, logger);
        session.Install(Entry).Unwrap();

        writer.WriteError = FakePatchWriter.CreateError();
        writer.FailWriteAt = Entry;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry, 1), Is.EqualTo(new byte[] { 0xE9 }));
            Assert.That(writer.Released, Is.Empty);
            Assert.That(Warnings(sink, "could not be put back"), Is.EqualTo(1));
            Assert.That(Warnings(sink, "stays in the target"), Is.EqualTo(1));
        }

        logger.Dispose();
    }

    // A failed restore in 1 of 2 pools keeps that pool and still releases the other.
    [Test]
    public void Dispose_OneFailedRestoreOfTwoPools_ReleasesOnlyTheOtherPool()
    {
        var (memory, writer, far) = FarTarget();
        EntryReadFailure failing = new(memory, Entry);
        PatchSession session = new(failing, writer);
        session.Install(Entry).Unwrap();
        var farBase = far - 0x20000;
        writer.NextBase = farBase;
        session.Install(far).Unwrap();

        failing.Fail = true;
        session.Dispose();

        Assert.That(writer.Released, Is.EquivalentTo([farBase]));
    }

    // A redirection whose write failed may stand at the entry all the same, so Dispose writes its entry back.
    [Test]
    public void Dispose_AnAttemptedRedirection_PutsTheEntryBackAndReleasesThePool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        writer.WriteError = FakePatchWriter.CreateError();
        writer.FailWriteAt = Entry;
        PatchSession session = new(memory, writer);
        Assert.That(session.Install(Entry).IsErr, Is.True);
        int written = writer.Written.Count;

        writer.WriteError = null;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Written, Has.Count.EqualTo(written + 1));
            Assert.That(writer.Written[^1].Address, Is.EqualTo(Entry));
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.EquivalentTo([Base]));
        }
    }

    // A retry after a redirect write that missed the target writes the entry back once, since the entry jumps into the
    // 2nd stub alone.
    [Test]
    public void Dispose_ARetryAfterAFailedRedirect_PutsTheEntryBackOnce()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        var (logger, sink) = Logged();
        writer.WriteError = FakePatchWriter.CreateError();
        writer.FailWriteAt = Entry;
        PatchSession session = new(memory, writer, logger);
        Assert.That(session.Install(Entry).IsErr, Is.True);

        writer.WriteError = null;
        session.Install(Entry).Unwrap();
        int written = writer.Written.Count;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Written, Has.Count.EqualTo(written + 1));
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(Warnings(sink, "stays in the target"), Is.Zero);
            Assert.That(writer.Released, Is.EquivalentTo([Base]));
        }

        logger.Dispose();
    }

    // An attempted redirection that can't be put back either keeps its pool and is logged once.
    [Test]
    public void Dispose_AnAttemptedRedirectionThatCannotBePutBack_KeepsItsPool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        writer.WriteError = FakePatchWriter.CreateError();
        writer.FailWriteAt = Entry;
        var (logger, sink) = Logged();
        PatchSession session = new(memory, writer, logger);

        var result = session.Install(Entry);
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(session.Patches, Is.Empty);
            Assert.That(Warnings(sink, $"the method at {Entry} still jumps into it"), Is.EqualTo(1));
            Assert.That(writer.Released, Is.Empty);
        }

        logger.Dispose();
    }

    // A thread stopped inside the pool's stub keeps the pool, though the entry is put back.
    [Test]
    public void Dispose_AThreadInsideThePool_KeepsIt()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        var (logger, sink) = Logged();
        PatchSession session = new(memory, writer, logger);
        var patch = session.Install(Entry).Unwrap();

        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(patch.Slot);
        writer.Suspension = suspension;
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.Empty);
            Assert.That(Warnings(sink, "a thread stands inside it"), Is.EqualTo(1));
        }

        logger.Dispose();
    }

    // A thread stopped on the pool's 1st byte counts as inside it, unlike a thread on an entry's 1st byte.
    [Test]
    public void Dispose_AThreadAtThePoolBase_KeepsIt()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();

        FakeThreadSuspension suspension = new();
        suspension.Positions.Add(Base);
        writer.Suspension = suspension;
        session.Dispose();

        Assert.That(writer.Released, Is.Empty);
    }

    // A release-time suspension that missed a thread keeps the pool, since a pool freed under that thread is a crash.
    [Test]
    public void Dispose_AnIncompleteSuspensionAtRelease_KeepsThePool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        var (logger, sink) = Logged();
        PatchSession session = new(memory, writer, logger);
        session.Install(Entry).Unwrap();

        FakeThreadSuspension suspension = new();
        writer.Suspension = suspension;
        int calls = 0;
        writer.OnSuspend = () =>
        {
            if (++calls == 3)
            {
                suspension.IsComplete = false;
            }
        };

        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.Empty);
            Assert.That(Warnings(sink, "not every thread could be suspended"), Is.EqualTo(1));
        }

        logger.Dispose();
    }

    // A release-time suspension that fails keeps every pool, since none of them could be checked.
    [Test]
    public void Dispose_ASuspendThatFailsAtRelease_KeepsEveryPool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        var (logger, sink) = Logged();
        PatchSession session = new(memory, writer, logger);
        session.Install(Entry).Unwrap();

        int calls = 0;
        writer.OnSuspend = () =>
        {
            if (++calls == 2)
            {
                writer.SuspendError = FakePatchWriter.CreateError();
            }
        };

        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Prologue));
            Assert.That(writer.Released, Is.Empty);
            Assert.That(Warnings(sink, "1 pool(s)"), Is.EqualTo(1));
        }

        logger.Dispose();
    }

    // A release that fails logs a warning and Dispose still finishes without throwing.
    [Test]
    public void Dispose_AReleaseThatFails_WarnsAndFinishes()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        var (logger, sink) = Logged();
        PatchSession session = new(memory, writer, logger);
        session.Install(Entry).Unwrap();

        writer.ReleaseError = FakePatchWriter.CreateError();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(session.Dispose, Throws.Nothing);
            Assert.That(writer.Released, Is.EquivalentTo([Base]));
            Assert.That(Warnings(sink, "could not be released"), Is.EqualTo(1));
        }

        logger.Dispose();
    }

    // A session with no patch, attempt or pool touches the target not at all.
    [Test]
    public void Dispose_AnEmptySession_SuspendsNothing()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession session = new(memory, writer);

        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Suspensions, Is.Zero);
            Assert.That(writer.Written, Is.Empty);
            Assert.That(writer.Released, Is.Empty);
        }
    }

    // A pool whose only install gave its slot back is still released, so a session with only a pool isn't empty.
    [Test]
    public void Dispose_APoolWithNoPatches_ReleasesIt()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        writer.SuspendError = FakePatchWriter.CreateError();
        PatchSession session = new(memory, writer);
        Assert.That(session.Install(Entry).IsErr, Is.True);

        writer.SuspendError = null;
        session.Dispose();

        Assert.That(writer.Released, Is.EquivalentTo([Base]));
    }

    // A 2nd Dispose suspends and writes nothing more.
    [Test]
    public void Dispose_ASessionAlreadyClosed_IsHarmless()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();
        session.Dispose();
        int suspensions = writer.Suspensions;
        int written = writer.Written.Count;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(session.Dispose, Throws.Nothing);
            Assert.That(writer.Suspensions, Is.EqualTo(suspensions));
            Assert.That(writer.Written, Has.Count.EqualTo(written));
        }
    }

    // The suspension taken for the whole unload is disposed even when a restore failed.
    [Test]
    public void Dispose_AFailedRestore_DisposesTheUnloadSuspension()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        EntryReadFailure failing = new(memory, Entry);
        PatchSession session = new(failing, writer);
        session.Install(Entry).Unwrap();

        FakeThreadSuspension unload = new();
        writer.Suspension = unload;
        int calls = 0;
        writer.OnSuspend = () =>
        {
            if (++calls == 2)
            {
                writer.Suspension = new FakeThreadSuspension();
            }
        };

        failing.Fail = true;
        session.Dispose();

        Assert.That(unload.Disposed, Is.True);
    }
}
