using AslHelp.Memory.Remote.Errors;

using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class RemotePatchWriterTests
{
    private const int PageSize = 0x1000;

    private Process _process = null!;
    private RemoteProcessMemory _memory = null!;
    private RemotePatchWriter _writer = null!;
    private Address _anchor;

    [SetUp]
    public void SetUp()
    {
        _process = Process.GetCurrentProcess();
        _memory = new(_process);
        _writer = new(_memory);
        _anchor = new((ulong)_process.MainModule!.BaseAddress);
    }

    [TearDown]
    public void TearDown()
    {
        _writer.Dispose();
        _memory.Dispose();
        _process.Dispose();
    }

    // Reserve returns a block a relative jump from the anchor reaches, readable and executable.
    [Test]
    public void Reserve_NearMainModule_ReturnsReachableExecutableBlock()
    {
        var reservation = _writer.Reserve(_anchor, PageSize).Unwrap();

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Math.Abs(reservation - _anchor), Is.LessThan(int.MaxValue));
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // Protect makes a reservation writable, so a plain write goes through.
    [Test]
    public void Protect_ReadWrite_AllowsAPlainWrite()
    {
        var reservation = _writer.Reserve(_anchor, PageSize).Unwrap();

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(_writer.Protect(reservation, PageSize, MemoryProtection.Read | MemoryProtection.Write).IsOk, Is.True);
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Write));
                Assert.That(_memory.Write(reservation, [1, 2, 3]).IsOk, Is.True);
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // WriteCode puts the bytes into a reservation that is not writable and leaves it readable and executable.
    [Test]
    public void WriteCode_IntoReservation_ReadsBackTheBytes()
    {
        var reservation = _writer.Reserve(_anchor, PageSize).Unwrap();

        try
        {
            byte[] read = new byte[4];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(_writer.WriteCode(reservation + 0x10, [0x90, 0x90, 0xCC, 0xC3]).IsOk, Is.True);
                Assert.That(_memory.Read(reservation + 0x10, read).IsOk, Is.True);
                Assert.That(read, Is.EqualTo(new byte[] { 0x90, 0x90, 0xCC, 0xC3 }));
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // WriteCode across 2 pages with different protection puts each page's own protection back.
    [Test]
    public void WriteCode_AcrossTwoPages_RestoresEachPageProtection()
    {
        var reservation = _writer.Reserve(_anchor, 2 * PageSize).Unwrap();

        try
        {
            _writer.Protect(reservation, PageSize, MemoryProtection.Read).Unwrap();

            var at = reservation + (PageSize - 2);
            byte[] read = new byte[4];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(_writer.WriteCode(at, [1, 2, 3, 4]).IsOk, Is.True);
                Assert.That(_memory.Read(at, read).IsOk, Is.True);
                Assert.That(read, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(ProtectionAt(reservation), Is.EqualTo(MemoryProtection.Read));
                Assert.That(ProtectionAt(reservation + PageSize), Is.EqualTo(MemoryProtection.Read | MemoryProtection.Execute));
            }
        }
        finally
        {
            _ = _writer.Release(reservation);
        }
    }

    // Release frees a reservation, so a second release of it fails.
    [Test]
    public void Release_Reservation_FreesTheBlock()
    {
        var reservation = _writer.Reserve(_anchor, PageSize).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_writer.Release(reservation).IsOk, Is.True);
            Assert.That(_memory.GetRegions(new(reservation, PageSize)).Unwrap().Any(region => region.Range.Contains(reservation)), Is.False);
            Assert.That(_writer.Release(reservation).IsOk, Is.False);
        }
    }

    // Every member returns the released handle error after Dispose rather than throwing.
    [Test]
    public void Members_AfterDispose_ReturnHandleReleased()
    {
        _writer.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_writer.Reserve(_anchor, PageSize).UnwrapErr(), Is.EqualTo(RemotePatchError.HandleReleased()));
            Assert.That(_writer.Protect(_anchor, PageSize, MemoryProtection.Read).UnwrapErr(), Is.EqualTo(RemotePatchError.HandleReleased()));
            Assert.That(_writer.WriteCode(_anchor, [0x90]).UnwrapErr(), Is.EqualTo(RemotePatchError.HandleReleased()));
            Assert.That(_writer.Release(_anchor).UnwrapErr(), Is.EqualTo(RemotePatchError.HandleReleased()));
            Assert.That(_writer.SuspendThreads().UnwrapErr(), Is.EqualTo(RemotePatchError.HandleReleased()));
        }
    }

    // SuspendThreads refuses the current process, which would stop the asking thread for good.
    [Test]
    public void SuspendThreads_CurrentProcess_ReturnsCannotSuspendItself()
    {
        Assert.That(_writer.SuspendThreads().UnwrapErr(), Is.EqualTo(RemotePatchError.CannotSuspendItself()));
    }

    // SuspendThreads on a 32-bit child reads every thread and finds each one somewhere.
    [Test]
    public void SuspendThreads_Child32Bit_ReadsEveryThread()
    {
        using var child = FixtureChild.Start32Bit();
        using var writer = child.NewWriter();
        using var suspension = writer.SuspendThreads().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(suspension.IsComplete, Is.True);
            Assert.That(suspension.Any(at => at != Address.Zero), Is.True);
            Assert.That(suspension.AnyIn(new(Address.Zero, PageSize)), Is.False);
        }
    }

    private MemoryProtection ProtectionAt(Address address)
    {
        return _memory.GetRegions(new(address, 1)).Unwrap().Single(region => region.Range.Contains(address)).Protection;
    }
}
