using AslHelp.Memory.Pe;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public partial class PeReaderExtensionsTests
{
    private Process _process = null!;
    private RemoteProcessMemory _memory = null!;
    private MemoryModule _kernel32 = null!;

    [SetUp]
    public void SetUp()
    {
        _process = Process.GetCurrentProcess();
        _memory = new(_process);
        _kernel32 = _memory.GetModule("kernel32.dll").Unwrap();
    }

    [TearDown]
    public void TearDown()
    {
        _memory.Dispose();
        _process.Dispose();
    }

    // GetExport finds names across kernel32's sorted table at the address the host's loader returns.
    [TestCase("GetProcAddress")]
    [TestCase("ActivateActCtx")]
    [TestCase("CloseHandle")]
    [TestCase("VirtualAlloc")]
    [TestCase("WriteFile")]
    [TestCase("lstrlenW")]
    public void GetExport_Kernel32Names_MatchTheHostLoader(string name)
    {
        nint expected = HostExport((nint)_kernel32.Range.Start.Value, name);

        Assert.That(_memory.GetExport(_kernel32, name).Unwrap(), Is.EqualTo(new Address((ulong)expected)));
    }

    // A forwarded kernel32 export returns an error instead of the address of its forwarder string.
    [Test]
    public void GetExport_AForwardedExport_ReturnsAnError()
    {
        var export = _memory.GetExport(_kernel32, "HeapAlloc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.IsErr, Is.True);
            Assert.That(export.Error!.Message, Does.Contain("forwarded"));
        }
    }

    // A name kernel32 doesn't export returns an error.
    [Test]
    public void GetExport_AMissingName_ReturnsAnError()
    {
        Assert.That(_memory.GetExport(_kernel32, "NoSuchFunction").IsErr, Is.True);
    }

    // The export entry of kernel32's data directory lies inside the module.
    [Test]
    public void GetDataDirectory_Kernel32Exports_LieInsideTheModule()
    {
        var directory = _memory.GetDataDirectory(_kernel32, PeDirectory.Export).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(directory.VirtualAddress, Is.Positive);
            Assert.That(directory.VirtualAddress + directory.Size, Is.LessThanOrEqualTo(_kernel32.Range.Size));
        }
    }

    // The export entry read from memory matches the one PEReader reads from kernel32's file on disk.
    [Test]
    public void GetDataDirectory_Kernel32Exports_MatchTheFileOnDisk()
    {
        using var stream = File.OpenRead(_kernel32.Path!);
        using PEReader file = new(stream);
        var expected = file.PEHeaders.PEHeader!.ExportTableDirectory;

        var directory = _memory.GetDataDirectory(_kernel32, PeDirectory.Export).Unwrap();

        Assert.That(directory, Is.EqualTo(new PeDataDirectory((uint)expected.RelativeVirtualAddress, (uint)expected.Size)));
    }

    // .NET resolves the export with GetProcAddress. .NET Framework has no NativeLibrary, so it calls GetProcAddress
    // itself.
    private static nint HostExport(nint module, string name)
    {
#if NET
        return NativeLibrary.GetExport(module, name);
#else
        return NativeMethods.GetProcAddress(module, name);
#endif
    }

#if !NET
    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern nint GetProcAddress(nint module, [MarshalAs(UnmanagedType.LPStr)] string name);
    }
#endif
}
