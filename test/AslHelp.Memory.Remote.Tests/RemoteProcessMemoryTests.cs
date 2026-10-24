using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

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
                Assert.That(_memory.Read(AddressOf(handle), read).IsOk, Is.True);
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
            Assert.That(_memory.Read<int>(AddressOf(handle)).Unwrap(), Is.EqualTo(0x12345678));
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
            var address = AddressOf(handle);
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

    // GetRegions reports the managed heap under a pinned array as readable and writable.
    [Test]
    public void GetRegions_PinnedBytes_ReportsReadWrite()
    {
        byte[] bytes = new byte[16];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            var address = AddressOf(handle);

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

    // Read of an unmapped low address returns an error instead of throwing.
    [Test]
    public void Read_UnmappedAddress_ReturnsAnError()
    {
        Assert.That(_memory.Read<int>(new Address(0x10)).IsErr, Is.True);
    }

    // Write to an unmapped low address returns an error instead of throwing.
    [Test]
    public void Write_UnmappedAddress_ReturnsAnError()
    {
        Assert.That(_memory.Write(new Address(0x10), [1]).IsErr, Is.True);
    }

    // Every call after Dispose throws.
    [Test]
    public void Calls_AfterDispose_Throw()
    {
        _memory.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => _memory.Read(new Address(0x10), new byte[1]), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => _memory.Write(new Address(0x10), [1]), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => _memory.GetRegions(new(new Address(0x10), 1)), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => _memory.GetModules(), Throws.TypeOf<ObjectDisposedException>());
        }
    }

    // The constructor refuses a null process.
    [Test]
    public void Constructor_NullProcess_Throws()
    {
        Assert.That(() => new RemoteProcessMemory(null!), Throws.ArgumentNullException);
    }

    private static Address AddressOf(GCHandle handle)
    {
        return new((ulong)handle.AddrOfPinnedObject());
    }
}
