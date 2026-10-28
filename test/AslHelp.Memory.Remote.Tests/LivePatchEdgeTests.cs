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
public class LivePatchEdgeTests
{
    private const int PageSize = 0x1000;
    private const int Granularity = 0x10000;

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

    // A stub that captures a register, a stack slot and a float register runs in the child and leaves a whole payload.
    [Test]
    public void Install_CapturingLayoutOnTick_CountsAndCapturesAWholeCall()
    {
        CaptureLayout layout = new(
        [
            new(CaptureSource.Register, 0, CaptureLayout.PayloadAt, 8),
            new(CaptureSource.Stack, 0, CaptureLayout.PayloadAt + 8, 8),
            new(CaptureSource.FloatRegister, 0, CaptureLayout.PayloadAt + 16, 8),
        ]);

        using var session = new PatchSession(_child.Memory, _writer);

        var patch = session.Install(_child.Tick, layout).Unwrap();
        ulong first = patch.ReadCount(_child.Memory).Unwrap();
        bool rose = _child.WaitForCount(patch, first + 4, _riseTimeout);

        byte[] payload = new byte[24];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rose, Is.True);
            Assert.That(patch.Captures, Is.True);
            Assert.That(patch.TryReadCall(_child.Memory, payload).Unwrap(), Is.True);
            Assert.That(_child.Process.HasExited, Is.False);
        }
    }

    // Reserve near the top of user space returns a block below the anchor, because nothing above it fits.
    [Test]
    public void Reserve_AnchorAtTopOfUserSpace_ReturnsABlockBelowWithinReach()
    {
        var anchor = new Address(0x7FFF_FFFF_0000);
        var reservation = _writer.Reserve(anchor, PageSize).Unwrap();

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(reservation.Value, Is.LessThan(anchor.Value));
                Assert.That(anchor - reservation, Is.LessThan(int.MaxValue));
                Assert.That(reservation.Value % Granularity, Is.Zero);
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // Reserve refuses an anchor past the signed address space instead of searching or wrapping.
    [TestCase(0xFFFF_FFFF_FFFF_0000UL)]
    [TestCase(0x8000_0000_0000_0000UL)]
    public void Reserve_AnchorPastTheAddressSpace_ReturnsOutOfReach(ulong value)
    {
        var anchor = new Address(value);

        Assert.That(_writer.Reserve(anchor, PageSize).UnwrapErr(), Is.EqualTo(RemotePatchError.OutOfReach(anchor)));
    }

    // Reserve far above user space searches both ways, finds no free block in reach and returns the refusal.
    [Test]
    public void Reserve_AnchorFarAboveUserSpace_SearchesAndReturnsOutOfReach()
    {
        var anchor = new Address(0x4000_0000_0000_0000);

        Assert.That(_writer.Reserve(anchor, PageSize).UnwrapErr(), Is.EqualTo(RemotePatchError.OutOfReach(anchor)));
    }

    // Reserve of 3 blocks near Tick commits the whole range, down to its last page.
    [Test]
    public void Reserve_ThreeBlocksNearTick_CommitsEveryPageExecutable()
    {
        const int Size = 3 * Granularity;

        var reservation = _writer.Reserve(_child.Tick, Size).Unwrap();

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Math.Abs(reservation - _child.Tick), Is.LessThan(int.MaxValue));
                Assert.That(Math.Abs(reservation + Size - _child.Tick), Is.LessThan(int.MaxValue));
                Assert.That(ProtectionAt(reservation + (Size - PageSize)), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // WriteCode that runs off a 1-page reservation is refused and leaves the 1st page unwritten and executable.
    [Test]
    public void WriteCode_PastTheReservationEnd_ReturnsNotMadeExecutableAndRestoresTheFirstPage()
    {
        var reservation = _writer.Reserve(_child.Tick, PageSize).Unwrap();

        try
        {
            var at = reservation + (PageSize - 2);
            var result = _writer.WriteCode(at, [1, 2, 3, 4]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.UnwrapErr().Message, Does.StartWith($"0x{at.Value:X} could not be made executable"));
                Assert.That(_child.Memory.ReadArray<byte>(at, 2).Unwrap(), Is.EqualTo(new byte[2]));
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // WriteCode of 0 bytes succeeds and touches neither the bytes nor the protection of Tick.
    [Test]
    public void WriteCode_EmptyBufferAtTick_ReturnsOkAndChangesNothing()
    {
        var protection = ProtectionAt(_child.Tick);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_writer.WriteCode(_child.Tick, []).IsOk, Is.True);
            Assert.That(ReadWord(_child.Tick).Unwrap(), Is.EqualTo(_before));
            Assert.That(ProtectionAt(_child.Tick), Is.EqualTo(protection));
        }
    }

    // Protect on a page nothing reserved returns the refusal rather than throwing.
    [Test]
    public void Protect_UnreservedPage_ReturnsNotProtected()
    {
        var at = new Address(0x7FFF_FFFF_0000);

        Assert.That(
            _writer.Protect(at, PageSize, MemoryProtection.Read).UnwrapErr().Message,
            Does.StartWith($"The protection at 0x{at.Value:X} could not be changed"));
    }

    // Release of a page inside a reservation is refused and the block keeps its protection.
    [Test]
    public void Release_PageInsideAReservation_ReturnsNotReleasedAndKeepsTheBlock()
    {
        var reservation = _writer.Reserve(_child.Tick, 2 * PageSize).Unwrap();

        try
        {
            var inside = reservation + PageSize;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    _writer.Release(inside).UnwrapErr().Message,
                    Does.StartWith($"The reservation at 0x{inside.Value:X} could not be released"));
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // SuspendThreads on a child that exited finds no thread of its id in the snapshot.
    [Test]
    public void SuspendThreads_ChildExited_ReturnsNoThreads()
    {
        _child.Process.Kill();
        _child.Process.WaitForExit();

        Assert.That(_writer.SuspendThreads().UnwrapErr(), Is.EqualTo(RemotePatchError.NoThreads()));
    }

    // Dispose of a session whose child died under a patch empties the ledger without throwing and refuses a later Install.
    [Test]
    public void Dispose_ChildExitedUnderAPatch_EmptiesTheLedgerAndClosesTheSession()
    {
        var session = new PatchSession(_child.Memory, _writer);
        _ = session.Install(_child.Tick).Unwrap();

        _child.Process.Kill();
        _child.Process.WaitForExit();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(session.Dispose, Throws.Nothing);
            Assert.That(session.Patches, Is.Empty);
            Assert.That(session.Install(_child.Tick).UnwrapErr().Message, Does.Contain("closed"));
        }
    }

    // Install inside the session's own suspension nests a 2nd suspension and the count rises once both end.
    [Test]
    public void Install_InsideSessionSuspension_RedirectsAndCountsOnceResumed()
    {
        using var session = new PatchSession(_child.Memory, _writer);

        CodePatch patch;
        ulong held;
        ulong heldLater;
        bool suspended;

        using (var batch = session.SuspendThreads())
        {
            suspended = batch is not null;

            patch = session.Install(_child.Tick).Unwrap();
            held = patch.ReadCount(_child.Memory).Unwrap();
            Thread.Sleep(50);
            heldLater = patch.ReadCount(_child.Memory).Unwrap();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suspended, Is.True);
            Assert.That(heldLater, Is.EqualTo(held));
            Assert.That(_child.WaitForCount(patch, heldLater, _riseTimeout), Is.True);
        }
    }

    // A session after one that put Tick back installs a fresh patch rather than adopting a stale page.
    [Test]
    public void Install_AfterEarlierSessionRestoredTick_InstallsFreshAndCounts()
    {
        var earlier = new PatchSession(_child.Memory, _writer);
        _ = earlier.Install(_child.Tick).Unwrap();
        earlier.Dispose();

        using var later = new PatchSession(_child.Memory, _writer);
        var patch = later.Install(_child.Tick).Unwrap();
        ulong first = patch.ReadCount(_child.Memory).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.WasAdopted, Is.False);
            Assert.That(_child.Memory.Read<byte>(_child.Tick).Unwrap(), Is.EqualTo((byte)0xE9));
            Assert.That(_child.WaitForCount(patch, first, _riseTimeout), Is.True);
        }
    }

    // AnyIn over a 0-byte range just below a suspended thread finds no thread inside.
    [Test]
    public void AnyIn_EmptyRangeBelowAThread_ReturnsFalse()
    {
        using var suspension = _writer.SuspendThreads().Unwrap();

        var ip = Address.Zero;
        _ = suspension.Any(at =>
        {
            ip = at;
            return at != Address.Zero;
        });

        Assert.That(suspension.AnyIn(new(ip - 1, 0)), Is.False);
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
