using AslHelp.Memory.Patching;

using NUnit.Framework;
using System;
using System.Linq;
using System.Runtime.Versioning;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class Wow64PatchWriterTests
{
    private const int PageSize = 0x1000;
    private const int Granularity = 0x10000;
    private const ulong TwoGb = 0x8000_0000;
    private const ulong FourGb = 0x1_0000_0000;

    private static readonly TimeSpan _riseTimeout = TimeSpan.FromSeconds(2);

    private FixtureChild _child = null!;
    private RemotePatchWriter _writer = null!;
    private byte[] _before = null!;

    [SetUp]
    public void SetUp()
    {
        _child = FixtureChild.Start32Bit();
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

    // The 32-bit child reports 4-byte pointers, which is what the writer caps its search with.
    [Test]
    public void Layout_Child32Bit_Has4BytePointers()
    {
        Assert.That(_child.Memory.Layout.PointerSize, Is.EqualTo(4));
    }

    // Reserve near the 32-bit child's Tick returns a block below 4 GB that a relative jump reaches.
    [Test]
    public void Reserve_NearTick_ReturnsABlockBelow4GbWithinReach()
    {
        var reservation = _writer.Reserve(_child.Tick, PageSize).Unwrap();

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(reservation.Value + PageSize, Is.LessThanOrEqualTo(FourGb));
                Assert.That(Math.Abs(reservation - _child.Tick), Is.LessThan(int.MaxValue));
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // Reserve near the top of a 32-bit child's 2 GB user space returns a block that ends below 2 GB, because the fixture is not large address aware.
    [TestCase(0x7000_0000UL)]
    [TestCase(0x7FFD_0000UL)]
    [TestCase(0x7FFE_FFFFUL)]
    public void Reserve_AnchorNearTopOf2GbUserSpace_ReturnsABlockThatEndsBelow2Gb(ulong value)
    {
        const int Size = 2 * Granularity;

        var anchor = new Address(value);
        var reservation = _writer.Reserve(anchor, Size).Unwrap();

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(reservation.Value + Size, Is.LessThanOrEqualTo(TwoGb));
                Assert.That(Math.Abs(anchor - reservation), Is.LessThan(int.MaxValue));
                Assert.That(ProtectionAt(reservation + (Size - PageSize)), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // Reserve refuses an anchor in the top page under 4 GB, since a 32-bit target can't point past it.
    [TestCase(0xFFFF_F000UL)]
    [TestCase(0xFFFF_FFFFUL)]
    [TestCase(FourGb)]
    public void Reserve_AnchorInOrPastTheTopPage_ReturnsOutOfReach(ulong value)
    {
        var anchor = new Address(value);

        Assert.That(_writer.Reserve(anchor, PageSize).UnwrapErr(), Is.EqualTo(Errors.RemotePatchError.OutOfReach(anchor)));
    }

    // WriteCode into a 32-bit child's reservation reads back and leaves the page readable and executable.
    [Test]
    public void WriteCode_IntoReservation_ReadsBackTheBytes()
    {
        var reservation = _writer.Reserve(_child.Tick, PageSize).Unwrap();

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(_writer.WriteCode(reservation + 0x10, [0x90, 0x90, 0xCC, 0xC3]).IsOk, Is.True);
                Assert.That(_child.Memory.ReadArray<byte>(reservation + 0x10, 4).Unwrap(), Is.EqualTo(new byte[] { 0x90, 0x90, 0xCC, 0xC3 }));
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // A suspension of the 32-bit child reads every thread below 4 GB.
    [Test]
    public void SuspendThreads_Child32Bit_FindsEveryThreadBelow4Gb()
    {
        using var suspension = _writer.SuspendThreads().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suspension.Any(at => at.Value >= FourGb || at == Address.Zero), Is.False);
            Assert.That(suspension.IsComplete, Is.True);
        }
    }

    // Install redirects the 32-bit Tick entry into a stub that counts the calls the child keeps making.
    [Test]
    public void Install_Child32BitTick_RedirectsTheEntryAndCountRises()
    {
        using var session = new PatchSession(_child.Memory, _writer);

        var patch = session.Install(_child.Tick).Unwrap();
        ulong first = patch.ReadCount(_child.Memory).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_child.Memory.Read<byte>(_child.Tick).Unwrap(), Is.EqualTo((byte)0xE9));
            Assert.That(patch.Slot.Value, Is.LessThan(FourGb));
            Assert.That(_child.WaitForCount(patch, first, _riseTimeout), Is.True);
        }
    }

    // A 2nd session decodes the 32-bit jump back to the 1st one's slot and adopts the patch.
    [Test]
    public void Install_AnotherSessionOverTheChild32Bit_AdoptsThePatch()
    {
        using var owner = new PatchSession(_child.Memory, _writer);
        var installed = owner.Install(_child.Tick).Unwrap();

        using var other = new PatchSession(_child.Memory, _writer);
        var adopted = other.Install(_child.Tick).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(adopted.IsAdopted, Is.True);
            Assert.That(adopted.Slot, Is.EqualTo(installed.Slot));
            Assert.That(adopted.Record, Is.EqualTo(installed.Record));
        }
    }

    // A stub that captures 2 stack slots runs in the 32-bit child and leaves a whole payload.
    [Test]
    public void Install_Child32BitStackCaptures_CountsAndCapturesAWholeCall()
    {
        CaptureLayout layout = new(
        [
            new(CaptureSource.Stack, 0, CaptureLayout.PayloadOffset, 4),
            new(CaptureSource.Stack, 1, CaptureLayout.PayloadOffset + 4, 4),
        ]);

        using var session = new PatchSession(_child.Memory, _writer);

        var patch = session.Install(_child.Tick, layout).Unwrap();
        ulong first = patch.ReadCount(_child.Memory).Unwrap();
        bool rose = _child.WaitForCount(patch, first + 4, _riseTimeout);

        byte[] payload = new byte[8];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rose, Is.True);
            Assert.That(patch.HasCaptures, Is.True);
            Assert.That(patch.TryReadCall(_child.Memory, payload).Unwrap(), Is.True);
            Assert.That(_child.Process.HasExited, Is.False);
        }
    }

    // Install refuses a register capture on the 32-bit child and leaves Tick as it was.
    [Test]
    public void Install_Child32BitRegisterCapture_ReturnsNoRegistersAndLeavesTickAlone()
    {
        CaptureLayout layout = new([new(CaptureSource.Register, 0, CaptureLayout.PayloadOffset, 4)]);

        using var session = new PatchSession(_child.Memory, _writer);

        var result = session.Install(_child.Tick, layout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.UnwrapErr().Message, Does.Contain("no register to capture"));
            Assert.That(session.Patches, Is.Empty);
            Assert.That(ReadWord(_child.Tick).Unwrap(), Is.EqualTo(_before));
        }
    }

    // Dispose puts the 32-bit Tick bytes back and releases the pool while the child keeps running.
    [Test]
    public void Dispose_Child32BitPatch_PutsTheDisplacedBytesBackAndReleasesThePool()
    {
        var session = new PatchSession(_child.Memory, _writer);
        var patch = session.Install(_child.Tick).Unwrap();

        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReadWord(_child.Tick).Unwrap(), Is.EqualTo(_before));
            Assert.That(_child.Memory.Read<byte>(patch.Slot).IsOk, Is.False);
            Assert.That(_child.Process.HasExited, Is.False);
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
