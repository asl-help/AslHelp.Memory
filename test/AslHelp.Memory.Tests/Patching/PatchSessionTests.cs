using AslHelp.Logging;
using AslHelp.Memory;
using AslHelp.Memory.Patching;
using NUnit.Framework;
using static AslHelp.Memory.Tests.Patching.SessionTargets;

namespace AslHelp.Memory.Tests.Patching;

// Tests installing through a session against a BufferMemory that spans the pool's block and the entries past it. The
// writes go through a FakePatchWriter backed by that memory, so no process is written and no thread is suspended.
[TestFixture]
public class PatchSessionTests
{
    // A new session reports no patches, reserves nothing and suspends nothing.
    [Test]
    public void PatchSession_ANewOne_HasNoPatches()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(session.Patches, Is.Empty);
            Assert.That(writer.Reserved, Is.Empty);
            Assert.That(writer.Suspensions, Is.Zero);
        }
    }

    // A null memory or writer is refused before anything is touched.
    [Test]
    public void PatchSession_ANullMemoryOrWriter_Throws()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => new PatchSession(null!, writer), Throws.ArgumentNullException);
            Assert.That(() => new PatchSession(memory, null!), Throws.ArgumentNullException);
        }
    }

    // A null layout is refused before a pool is reserved.
    [Test]
    public void Install_ANullLayout_Throws()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => session.Install(Entry, null!), Throws.ArgumentNullException);
            Assert.That(writer.Reserved, Is.Empty);
        }
    }

    // 1 install reserves 1 pool at the entry and writes the record, the stub and the jump at the entry.
    [Test]
    public void Install_AnEntry_ReservesAPoolAndRedirectsIt()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);

        var patch = session.Install(Entry).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Reserved, Has.Count.EqualTo(1));
            Assert.That(writer.Reserved[0].Anchor, Is.EqualTo(Entry));
            Assert.That(writer.Written, Has.Count.EqualTo(3));
            Assert.That(writer.Written[2].Address, Is.EqualTo(Entry));
            Assert.That(ReadAt(memory, Entry, 5), Is.EqualTo(Jump(Entry, patch.Slot)));
            Assert.That(session.Patches, Is.EquivalentTo([patch]));
            Assert.That(patch.IsAdopted, Is.False);
        }
    }

    // A 2nd install at the same entry returns the standing patch and writes nothing more.
    [Test]
    public void Install_TheSameEntryTwice_ReturnsTheStandingPatch()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);
        var first = session.Install(Entry).Unwrap();
        int written = writer.Written.Count;

        var second = session.Install(Entry).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(writer.Reserved, Has.Count.EqualTo(1));
            Assert.That(writer.Written, Has.Count.EqualTo(written));
            Assert.That(session.Patches, Has.Count.EqualTo(1));
        }
    }

    // A session over an entry another session patched adopts that patch and reserves nothing.
    [Test]
    public void Install_AnEntryAnotherSessionPatched_AdoptsItWithoutReserving()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession first = new(memory, writer);
        var installed = first.Install(Entry).Unwrap();
        int written = writer.Written.Count;

        RecordingSink sink = new();
        using Logger logger = new();
        logger.Add(sink);
        using PatchSession second = new(memory, writer, logger);

        var adopted = second.Install(Entry).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(adopted.IsAdopted, Is.True);
            Assert.That(adopted.Slot, Is.EqualTo(installed.Slot));
            Assert.That(writer.Reserved, Has.Count.EqualTo(1));
            Assert.That(writer.Written, Has.Count.EqualTo(written));
            Assert.That(sink.Lines, Has.Count.EqualTo(1));
            Assert.That(sink.Lines[0].Level, Is.EqualTo(LogLevel.Debug));
            Assert.That(sink.Lines[0].Message, Does.Contain(Entry.ToString()));
        }
    }

    // 2 entries in reach of 1 pool take consecutive slots out of 1 reservation.
    [Test]
    public void Install_TwoEntriesInReach_ShareOnePool()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue, 0x140);
        var other = Entry + 0x100;
        memory.Write(other, Prologue).Unwrap();
        using PatchSession session = new(memory, writer);

        var first = session.Install(Entry).Unwrap();
        var second = session.Install(other).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Reserved, Has.Count.EqualTo(1));
            Assert.That(second.Slot, Is.EqualTo(first.Slot + CodePatch.SlotSize));
            Assert.That(second.Record, Is.EqualTo(first.Record + CaptureLayout.RecordSize));
        }
    }

    // An entry out of a relative jump's reach of the 1st pool gets a 2nd reservation anchored at it.
    [Test]
    public void Install_AnEntryOutOfReachOfThePool_ReservesANewPool()
    {
        var (memory, writer, far) = FarTarget();
        using PatchSession session = new(memory, writer);
        var first = session.Install(Entry).Unwrap();

        writer.NextBase = far - 0x20000;
        var second = session.Install(far).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(writer.Reserved, Has.Count.EqualTo(2));
            Assert.That(writer.Reserved[1].Anchor, Is.EqualTo(far));
            Assert.That(session.Patches, Is.EquivalentTo([first, second]));
        }
    }

    // An install after every slot of the pool is handed out reserves a 2nd pool instead of failing.
    [Test]
    public void Install_AFullPool_ReservesANewPool()
    {
        const int SlotCount = 0x6000 / CaptureLayout.RecordSize;

        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue, (SlotCount * 8) + 0x40);
        using PatchSession session = new(memory, writer);

        for (int i = 0; i < SlotCount; i++)
        {
            memory.Write(Entry + (i * 8), Prologue).Unwrap();
            session.Install(Entry + (i * 8)).Unwrap();
        }

        var last = Entry + (SlotCount * 8);
        memory.Write(last, Prologue).Unwrap();
        writer.NextBase = Base + PoolSize;

        var result = session.Install(last);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(writer.Reserved, Has.Count.EqualTo(2));
            Assert.That(session.Patches, Has.Count.EqualTo(SlotCount + 1));
        }
    }

    // A 32-bit target gets the same 5-byte jump and keeps the 6th displaced byte in place.
    [Test]
    public void Install_A32BitTarget_RedirectsWithTheX86Stub()
    {
        var (memory, writer) = Target(MemoryLayout.X86, Base32, Entry32, Prologue32);
        using PatchSession session = new(memory, writer);

        var patch = session.Install(Entry32).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadAt(memory, Entry32, 5), Is.EqualTo(Jump(Entry32, patch.Slot)));
            Assert.That(ReadAt(memory, Entry32 + 5, 1), Is.EqualTo(new byte[] { 0x08 }));
        }
    }

    // A target with 2-byte pointers is refused before a pool is reserved.
    [Test]
    public void Install_ATargetWithTwoBytePointers_RefusesBeforeReserving()
    {
        var (memory, writer) = Target(new MemoryLayout(2, ByteOrder.LittleEndian), Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);

        var result = session.Install(Entry);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("can't be patched"));
            Assert.That(writer.Reserved, Is.Empty);
        }
    }

    // Address 0 is refused before anything is read, reserved or suspended.
    [Test]
    public void Install_AddressZero_RefusesWithoutReserving()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        using PatchSession session = new(memory, writer);

        var result = session.Install(Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("no compiled code"));
            Assert.That(writer.Reserved, Is.Empty);
            Assert.That(writer.Suspensions, Is.Zero);
            Assert.That(session.Patches, Is.Empty);
        }
    }

    // An entry in the last word of the address space installs without an overflow escaping.
    [Test]
    public void Install_AnEntryInTheLastWordOfAddressSpace_Redirects()
    {
        var entry = new Address(ulong.MaxValue - 7);
        var (memory, writer) = Target(MemoryLayout.X64, new(0xFFFF_FFFF_FFFE_0000), entry, Prologue);
        using PatchSession session = new(memory, writer);

        var patch = session.Install(entry).Unwrap();

        Assert.That(ReadAt(memory, entry, 5), Is.EqualTo(Jump(entry, patch.Slot)));
    }

    // An entry that crosses an 8-byte boundary is refused by the plan before a pool is reserved.
    [Test]
    public void Install_AnEntryThatStraddlesAWord_ReturnsThePlanError()
    {
        var entry = Entry + 4;
        var (memory, writer) = Target(MemoryLayout.X64, Base, entry, Prologue);
        using PatchSession session = new(memory, writer);

        var result = session.Install(entry);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("crosses an 8-byte boundary"));
            Assert.That(writer.Reserved, Is.Empty);
        }
    }

    // A reservation that fails comes back as it is and the session keeps no pool or patch.
    [Test]
    public void Install_AReserveThatFails_ReturnsTheErrorAndKeepsNothing()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        var error = FakePatchWriter.CreateError();
        writer.ReserveError = error;
        PatchSession session = new(memory, writer);

        var result = session.Install(Entry);
        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(session.Patches, Is.Empty);
            Assert.That(writer.Suspensions, Is.Zero);
        }
    }

    // A redirect write that fails comes back as it is and leaves no patch in the session.
    [Test]
    public void Install_ARedirectWriteThatFails_ReturnsTheErrorAndKeepsNoPatch()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        var error = FakePatchWriter.CreateError();
        writer.WriteError = error;
        writer.FailWriteAt = Entry;
        using PatchSession session = new(memory, writer);

        var result = session.Install(Entry);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error, Is.SameAs(error));
            Assert.That(session.Patches, Is.Empty);
        }
    }

    // A disposed session refuses every install and suspension and touches the target no more.
    [Test]
    public void Install_AClosedSession_WritesNothingMore()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        PatchSession session = new(memory, writer);
        session.Dispose();

        var result = session.Install(new Address(0x1000));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("closed"));
            Assert.That(session.SuspendThreads(), Is.Null);
            Assert.That(session.Patches, Is.Empty);
            Assert.That(writer.Written, Is.Empty);
            Assert.That(writer.Suspensions, Is.Zero);
        }
    }

    // An open session hands back the writer's suspension, whose disposal resumes the threads.
    [Test]
    public void SuspendThreads_AnOpenSession_ReturnsTheWriterSuspension()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        FakeThreadSuspension suspension = new();
        writer.Suspension = suspension;
        using PatchSession session = new(memory, writer);

        var result = session.SuspendThreads();
        result?.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.SameAs(suspension));
            Assert.That(writer.Suspensions, Is.EqualTo(1));
            Assert.That(suspension.Disposed, Is.True);
        }
    }

    // A suspension that fails gives null and logs 1 warning with the error.
    [Test]
    public void SuspendThreads_ASuspendThatFails_ReturnsNullAndWarns()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue);
        writer.SuspendError = FakePatchWriter.CreateError("no threads today");
        RecordingSink sink = new();
        using Logger logger = new();
        logger.Add(sink);
        using PatchSession session = new(memory, writer, logger);

        var result = session.SuspendThreads();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(sink.Lines, Has.Count.EqualTo(1));
            Assert.That(sink.Lines[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(sink.Lines[0].Message, Does.Contain("no threads today"));
        }
    }

    // Each read of the patches is a new copy, so a later install leaves an earlier copy as it was.
    [Test]
    public void Patches_ACopy_DoesNotChangeWithALaterInstall()
    {
        var (memory, writer) = Target(MemoryLayout.X64, Base, Entry, Prologue, 0x140);
        var other = Entry + 0x100;
        memory.Write(other, Prologue).Unwrap();
        using PatchSession session = new(memory, writer);
        session.Install(Entry).Unwrap();

        var before = session.Patches;
        session.Install(other).Unwrap();
        var after = session.Patches;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(before, Has.Count.EqualTo(1));
            Assert.That(after, Has.Count.EqualTo(2));
        }
    }
}
