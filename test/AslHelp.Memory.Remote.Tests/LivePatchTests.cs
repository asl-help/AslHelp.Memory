using AslHelp.Memory.Patching;
using AslHelp.Memory.Remote.Errors;

using NUnit.Framework;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class LivePatchTests
{
    private const int PageSize = 0x1000;
    private const int PoolSize = 0x10000;

    // Matches the 6 record pages a pool puts before its code, so the 1st record of a pool is its base.
    private const int CodeBase = 6 * PageSize;

    private static readonly TimeSpan _riseTimeout = TimeSpan.FromSeconds(2);

    private FixtureChild _child = null!;
    private RemotePatchWriter _writer = null!;
    private byte[] _before = null!;

    [SetUp]
    public void SetUp()
    {
        _child = FixtureChild.Start();
        _writer = _child.NewWriter();
        _before = ReadWord(_child.Tick).Unwrap();
    }

    [TearDown]
    public void TearDown()
    {
        // Skips what SetUp never made, because an inconclusive start leaves both fields unset.
        _writer?.Dispose();
        _child?.Dispose();
    }

    // Install redirects the real Tick entry into a stub that counts the calls the child keeps making.
    [Test]
    public void Install_FixtureTick_RedirectsTheEntryAndCountRises()
    {
        using var session = new PatchSession(_child.Memory, _writer);

        var patch = session.Install(_child.Tick).Unwrap();
        ulong first = patch.ReadCount(_child.Memory).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_child.Memory.Read<byte>(_child.Tick).Unwrap(), Is.EqualTo((byte)0xE9));
            Assert.That(patch.WasAdopted, Is.False);
            Assert.That(_child.WaitForCount(patch, first, _riseTimeout), Is.True);
        }
    }

    // A 2nd Install of the same entry returns the patch already there.
    [Test]
    public void Install_SameEntryTwice_ReturnsTheExistingPatch()
    {
        using var session = new PatchSession(_child.Memory, _writer);

        var first = session.Install(_child.Tick).Unwrap();
        var second = session.Install(_child.Tick).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Slot, Is.EqualTo(first.Slot));
            Assert.That(second.Record, Is.EqualTo(first.Record));
            Assert.That(session.Patches, Has.Count.EqualTo(1));
        }
    }

    // A 2nd session adopts the 1st one's patch. Disposing the 2nd leaves the 1st one's pool reserved.
    [Test]
    public void Install_AnotherSessionOverTheSameChild_AdoptsThePatch()
    {
        var owner = new PatchSession(_child.Memory, _writer);
        var installed = owner.Install(_child.Tick).Unwrap();

        var other = new PatchSession(_child.Memory, _writer);
        var adopted = other.Install(_child.Tick).Unwrap();

        other.Dispose();
        bool keptAfterOther = _child.Memory.Read<byte>(installed.Slot).IsOk;

        owner.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(adopted.WasAdopted, Is.True);
            Assert.That(adopted.Slot, Is.EqualTo(installed.Slot));
            Assert.That(keptAfterOther, Is.True);
            Assert.That(_child.Memory.Read<byte>(installed.Slot).IsOk, Is.False);
        }
    }

    // Dispose puts the displaced bytes back and releases the pool while the child keeps running.
    [Test]
    public void Dispose_InstalledPatch_PutsTheDisplacedBytesBackAndReleasesThePool()
    {
        var session = new PatchSession(_child.Memory, _writer);
        var patch = session.Install(_child.Tick).Unwrap();

        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadWord(_child.Tick).Unwrap(), Is.EqualTo(_before));
            Assert.That(_child.Memory.Read<byte>(patch.Slot).IsOk, Is.False);
            Assert.That(patch.ReadCount(_child.Memory).IsOk, Is.False);
            Assert.That(_child.Process.HasExited, Is.False);
        }
    }

    // Install refuses address 0 before it reserves anything.
    [Test]
    public void Install_Address0_ReturnsNoCodeToRedirect()
    {
        using var session = new PatchSession(_child.Memory, _writer);

        var result = session.Install(Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.UnwrapErr().Message, Does.Contain("no compiled code"));
            Assert.That(session.Patches, Is.Empty);
        }
    }

    // Install refuses an unmapped entry when the plan reads it, so no write is tried.
    [Test]
    public void Install_UnmappedHighAddress_ReturnsUnreadableEntry()
    {
        using var session = new PatchSession(_child.Memory, _writer);

        var entry = new Address(0x7FFF_FFFF_0000);
        var result = session.Install(entry);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.UnwrapErr().Message, Is.EqualTo($"The code at {entry} couldn't be read."));
            Assert.That(session.Patches, Is.Empty);
        }
    }

    // SuspendThreads stops the looping child, finds each thread outside the patch and lets it run again on Dispose.
    [Test]
    public void SuspendThreads_FixtureLooping_StopsTheChildAndFindsEachThreadOutsideThePatch()
    {
        using var session = new PatchSession(_child.Memory, _writer);
        var patch = session.Install(_child.Tick).Unwrap();

        bool complete;
        bool anywhere;
        bool inFirstPage;
        bool inPool;
        ulong held;
        ulong heldLater;

        using (var suspension = _writer.SuspendThreads().Unwrap())
        {
            complete = suspension.IsComplete;
            anywhere = suspension.Any(at => at != Address.Zero);
            inFirstPage = suspension.AnyIn(new(Address.Zero, PageSize));
            inPool = suspension.AnyIn(new(patch.Record, PoolSize));

            held = patch.ReadCount(_child.Memory).Unwrap();
            Thread.Sleep(50);
            heldLater = patch.ReadCount(_child.Memory).Unwrap();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Slot - patch.Record, Is.EqualTo(CodeBase));
            Assert.That(complete, Is.True);
            Assert.That(anywhere, Is.True);
            Assert.That(inFirstPage, Is.False);
            Assert.That(inPool, Is.False);
            Assert.That(heldLater, Is.EqualTo(held));
            Assert.That(_child.WaitForCount(patch, heldLater, _riseTimeout), Is.True);
        }
    }

    // AnyIn leaves out the 1st byte of a range, so a thread standing at the start doesn't count as inside.
    [Test]
    public void AnyIn_ThreadAtRangeStart_ReturnsFalse()
    {
        using var suspension = _writer.SuspendThreads().Unwrap();

        var ip = Address.Zero;
        _ = suspension.Any(at =>
        {
            ip = at;
            return at != Address.Zero;
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ip, Is.Not.EqualTo(Address.Zero));
            Assert.That(suspension.AnyIn(new(ip, 1)), Is.False);
            Assert.That(suspension.AnyIn(new(ip - 1, 2)), Is.True);
        }
    }

    // A reservation near Tick takes code and then a plain write before it is released once.
    [Test]
    public void Reserve_NearTick_RoundTripsThroughProtectWriteCodeAndRelease()
    {
        var reservation = _writer.Reserve(_child.Tick, PageSize).Unwrap();
        byte[] read = new byte[4];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Math.Abs(reservation - _child.Tick), Is.LessThan(int.MaxValue));
            Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));

            Assert.That(_writer.WriteCode(reservation + 0x10, [0x90, 0x90, 0xCC, 0xC3]).IsOk, Is.True);
            Assert.That(_child.Memory.Read(reservation + 0x10, read).IsOk, Is.True);
            Assert.That(read, Is.EqualTo(new byte[] { 0x90, 0x90, 0xCC, 0xC3 }));
            Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));

            Assert.That(_writer.Protect(reservation, PageSize, MemoryProtection.Read | MemoryProtection.Write).IsOk, Is.True);
            Assert.That(_child.Memory.Write(reservation, [1, 2, 3]).IsOk, Is.True);

            Assert.That(_writer.Release(reservation).IsOk, Is.True);
            Assert.That(_writer.Release(reservation).IsOk, Is.False);
        }
    }

    // A session whose writer was disposed refuses the install and leaves the entry as it was.
    [Test]
    public void Install_WriterDisposed_ReturnsHandleReleased()
    {
        using var session = new PatchSession(_child.Memory, _writer);
        _writer.Dispose();

        var result = session.Install(_child.Tick);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.UnwrapErr(), Is.EqualTo(RemotePatchError.HandleReleased()));
            Assert.That(ReadWord(_child.Tick).Unwrap(), Is.EqualTo(_before));
        }
    }

    private Result<byte[]> ReadWord(Address address)
    {
        return _child.Memory.ReadArray<byte>(address, 8);
    }

    private MemoryProtection ProtectionAt(Address address)
    {
        return _child.Memory.GetRegions(new(address, 1)).Unwrap().Single(region => region.Range.Contains(address)).Protection;
    }
}
