using AslHelp.Logging;
using AslHelp.Memory.Remote.Errors;
using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.System.Memory;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class RemoteProcessMemoryTests
{
    private Process _process = null!;
    private RemoteProcessMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        _process = Process.GetCurrentProcess();
        _memory = new(_process);
    }

    [TearDown]
    public void TearDown()
    {
        _memory.Dispose();
        _process.Dispose();
    }

    // The layout matches the bitness of the test host.
    [Test]
    public void Layout_CurrentProcess_MatchesTheHost()
    {
        Assert.That(_memory.Layout.PointerSize, Is.EqualTo(IntPtr.Size));
    }

    // Read copies the bytes of a pinned managed array.
    [Test]
    public void Read_PinnedBytes_ReadsTheBytes()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            byte[] read = new byte[5];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(_memory.Read(GetAddress(handle), read).IsOk, Is.True);
                Assert.That(read, Is.EqualTo(bytes));
            }
        }
        finally
        {
            handle.Free();
        }
    }

    // Read of an int returns the value of a pinned managed int.
    [Test]
    public void Read_PinnedInt_ReadsTheValue()
    {
        int[] value = [0x12345678];
        var handle = GCHandle.Alloc(value, GCHandleType.Pinned);

        try
        {
            Assert.That(_memory.Read<int>(GetAddress(handle)).Unwrap(), Is.EqualTo(0x12345678));
        }
        finally
        {
            handle.Free();
        }
    }

    // Write changes the pinned managed array that a later read returns.
    [Test]
    public void Write_PinnedBytes_ReadsBackTheWrittenBytes()
    {
        byte[] bytes = new byte[4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            var address = GetAddress(handle);
            byte[] read = new byte[4];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(_memory.Write(address, [9, 8, 7, 6]).IsOk, Is.True);
                Assert.That(_memory.Read(address, read).IsOk, Is.True);
                Assert.That(read, Is.EqualTo(new byte[] { 9, 8, 7, 6 }));
                Assert.That(bytes, Is.EqualTo(new byte[] { 9, 8, 7, 6 }));
            }
        }
        finally
        {
            handle.Free();
        }
    }

    // GetRegions ends the list at the top of user memory, because Windows refuses a query above it as an invalid parameter.
    [Test]
    public void GetRegions_WholeAddressSpace_ListsUpToTheTop()
    {
        var regions = _memory.GetRegions(new(new Address(0), ulong.MaxValue)).Unwrap();

        Assert.That(regions, Has.Some.Matches<MemoryRegion>(region => region.Range.Contains(_memory.GetMainModule().Unwrap().Range.Start)));
    }

    // GetRegions reports the managed heap under a pinned array as readable and writable.
    [Test]
    public void GetRegions_PinnedBytes_ReportsReadWrite()
    {
        byte[] bytes = new byte[16];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            var address = GetAddress(handle);

            var regions = _memory.GetRegions(new(address, 16)).Unwrap();

            Assert.That(
                regions.Single(region => region.Range.Contains(address)).Protection,
                Is.EqualTo(MemoryProtection.Read | MemoryProtection.Write));
        }
        finally
        {
            handle.Free();
        }
    }

    // GetModules lists the main module of the test host and kernel32.dll.
    [Test]
    public void GetModules_CurrentProcess_ContainsMainModuleAndKernel32()
    {
        string mainModule = _process.MainModule!.ModuleName;

        var names = _memory.GetModules().Unwrap().Select(module => module.Name).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Has.Some.EqualTo(mainModule).IgnoreCase);
            Assert.That(names, Has.Some.EqualTo("kernel32.dll").IgnoreCase);
        }
    }

    // GetModule finds kernel32.dll by a name in a different case, with its full path and a non-empty range.
    [Test]
    public void GetModule_UppercaseKernel32_FindsTheModule()
    {
        var module = _memory.GetModule("KERNEL32.DLL").Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(module.Name, Is.EqualTo("kernel32.dll").IgnoreCase);
            Assert.That(module.Path, Does.EndWith(@"\kernel32.dll").IgnoreCase);
            Assert.That(module.Range.Size, Is.Positive);
        }
    }

    // The main module of the test host is its exe and is 1 of the listed modules.
    [Test]
    public void GetMainModule_CurrentProcess_IsInTheModuleList()
    {
        var module = _memory.GetMainModule().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(module.Path, Is.EqualTo(_process.MainModule!.FileName).IgnoreCase);
            Assert.That(_memory.GetModules().Unwrap(), Has.Member(module));
        }
    }

    // Read of an unmapped low address returns an error instead of throwing.
    [Test]
    public void Read_UnmappedAddress_ReturnsAnError()
    {
        Assert.That(_memory.Read<int>(new Address(0x10)).IsErr, Is.True);
    }

    // The error of a read Windows cut short gives the number of bytes it read.
    [Test]
    public void Message_IncompleteRead_GivesTheBytesRead()
    {
        RemoteUnreadableError error = new(new Address(0x1000), 8, 0, 3);

        Assert.That(error.Message, Is.EqualTo("Read only 3 of 8 bytes at 0x1000."));
    }

    // An execute-only page reads from another process and a region scan reaches it.
    [Test]
    public unsafe void Read_ExecuteOnlyPage_ReadsAndScansTheBytes()
    {
        var host = _process.SafeHandle;
        byte* page = (byte*)PInvoke.VirtualAllocEx(
            host,
            null,
            0x1000,
            VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
            PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

        Assert.That((nint)page, Is.Not.Zero);

        try
        {
            page[0] = 0xC3;
            page[1] = 0x5A;
            Assert.That((bool)PInvoke.VirtualProtectEx(host, page, 0x1000, PAGE_PROTECTION_FLAGS.PAGE_EXECUTE, out _), Is.True);

            Address address = new((ulong)page);
            byte[] read = new byte[2];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(_memory.Read(address, read).IsOk, Is.True);
                Assert.That(read, Is.EqualTo(new byte[] { 0xC3, 0x5A }));
                Assert.That(Scan.ScanRegions(_memory, new MemoryRange(address, 0x1000), ScanPattern.Parse("C3 5A")).Unwrap(), Is.EqualTo([address]));
            }
        }
        finally
        {
            PInvoke.VirtualFreeEx(host, page, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE);
        }
    }

    // A failed read logs the message of its error at trace level.
    [Test]
    public void Read_WithALogger_LogsTheFailedRead()
    {
        RecordingSink sink = new();
        using Logger logger = new();
        logger.Add(sink);
        using RemoteProcessMemory memory = new(_process, logger);

        var error = memory.Read(new Address(0x10), new byte[4]).UnwrapErr();

        Assert.That(sink.Messages, Is.EqualTo([error.Message]));
    }

    // Write to an unmapped low address returns an error instead of throwing.
    [Test]
    public void Write_UnmappedAddress_ReturnsAnError()
    {
        Assert.That(_memory.Write(new Address(0x10), [1]).IsErr, Is.True);
    }

    // Every call after Dispose returns the closed error, because a watcher can still read while the game exits.
    [Test]
    public void Calls_AfterDispose_ReturnTheClosedError()
    {
        _memory.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_memory.Read(new Address(0x10), new byte[1]).UnwrapErr().Message, Does.Contain("closed"));
            Assert.That(_memory.Write(new Address(0x10), [1]).UnwrapErr().Message, Does.Contain("closed"));
            Assert.That(_memory.GetRegions(new(new Address(0x10), 1)).UnwrapErr().Message, Does.Contain("closed"));
            Assert.That(_memory.GetModules().UnwrapErr().Message, Does.Contain("closed"));
            Assert.That(_memory.GetMainModule().UnwrapErr().Message, Does.Contain("closed"));
        }
    }

    // The constructor refuses a null process.
    [Test]
    public void Constructor_NullProcess_Throws()
    {
        Assert.That(() => new RemoteProcessMemory(null!), Throws.ArgumentNullException);
    }

    private static Address GetAddress(GCHandle handle)
    {
        return new((ulong)handle.AddrOfPinnedObject());
    }
}

file sealed class RecordingSink : ILogSink
{
    public List<string> Messages { get; } = [];

    public LogLevel MinimumLevel => LogLevel.Trace;

    public void Emit(in LogEvent e, int indent)
    {
        Messages.Add(e.Message);
    }

    public void Dispose()
    {
    }
}
