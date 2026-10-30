using AslHelp.Memory.Pe;
using AsmResolver.PE.File;
using NUnit.Framework;
using System;
using System.Buffers.Binary;

namespace AslHelp.Memory.Tests.Pe;

[TestFixture]
public class PeReaderExtensionsTests
{
    // The DOS header keeps the offset of the NT headers here, e_lfanew.
    private const int LfanewOffset = 0x3C;

    // The optional header's magic follows the 4-byte NT signature and the 20-byte file header.
    private const int MagicOffset = 24;

    private static readonly Address _base = new(0x7FF600000000);

    private static readonly string[] _sorted = ["Alpha", "Beta", "Delta", "Epsilon", "Gamma", "Zeta", "lower"];

    // Each name of a sorted table resolves through its ordinal, in a PE32 and in a PE32+ image.
    [Test]
    public void GetExport_EachNameOfASortedTable_ResolvesThroughItsOrdinal(
        [Values] bool is64Bit,
        [ValueSource(nameof(_sorted))] string name)
    {
        var image = PeImage.Create(is64Bit, _sorted);
        var (memory, module) = Load(image);

        Assert.That(memory.GetExport(module, name).Unwrap(), Is.EqualTo(_base + image.GetFunctionRva(name)));
    }

    // A name that isn't exported returns an error that names it.
    [TestCase("Aardvark")]
    [TestCase("Carrot")]
    [TestCase("Gamm")]
    [TestCase("Gammas")]
    [TestCase("zzz")]
    public void GetExport_AMissingName_ReturnsAnError(string name)
    {
        var (memory, module) = Load(PeImage.Create(true, _sorted));

        var export = memory.GetExport(module, name);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.IsErr, Is.True);
            Assert.That(export.Error!.Message, Does.Contain(name));
        }
    }

    // A name outside ASCII matches no export.
    [Test]
    public void GetExport_ANonAsciiName_ReturnsAnError()
    {
        var (memory, module) = Load(PeImage.Create(true, _sorted));

        Assert.That(memory.GetExport(module, "Bëta").IsErr, Is.True);
    }

    // A name the binary search misses in a table that isn't sorted is still found name by name.
    [Test]
    public void GetExport_AnUnsortedTable_FindsTheName()
    {
        var image = PeImage.Create(false, ["Zeta", "Alpha", "Gamma"]);
        var (memory, module) = Load(image);

        Assert.That(memory.GetExport(module, "Zeta").Unwrap(), Is.EqualTo(_base + image.GetFunctionRva("Zeta")));
    }

    // An export whose function points into the export directory is a forwarder and returns an error.
    [Test]
    public void GetExport_AForwardedExport_ReturnsAnError()
    {
        var (memory, module) = Load(PeImage.Create(true, _sorted, forwarded: "Delta"));

        var export = memory.GetExport(module, "Delta");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.IsErr, Is.True);
            Assert.That(export.Error!.Message, Does.Contain("forwarded"));
        }
    }

    // An image without an export directory returns an error.
    [Test]
    public void GetExport_NoExportDirectory_ReturnsAnError()
    {
        var image = PeImage.Create(
            true,
            _sorted,
            configure: file => file.OptionalHeader.SetDataDirectory(DataDirectoryIndex.ExportDirectory, default(DataDirectory)));
        var (memory, module) = Load(image);

        Assert.That(memory.GetExport(module, "Alpha").IsErr, Is.True);
    }

    // A bad DOS signature returns an error.
    [Test]
    public void GetExport_ABadDosSignature_ReturnsAnError()
    {
        var image = PeImage.Create(true, _sorted);
        image.Bytes[0] = (byte)'X';
        var (memory, module) = Load(image);

        var export = memory.GetExport(module, "Alpha");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.IsErr, Is.True);
            Assert.That(export.Error!.Message, Does.Contain("DOS"));
        }
    }

    // A bad NT signature returns an error.
    [Test]
    public void GetExport_ABadNtSignature_ReturnsAnError()
    {
        var image = PeImage.Create(false, _sorted);
        image.Bytes[image.File.DosHeader.NextHeaderOffset] = (byte)'X';
        var (memory, module) = Load(image);

        var export = memory.GetExport(module, "Alpha");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.IsErr, Is.True);
            Assert.That(export.Error!.Message, Does.Contain("NT"));
        }
    }

    // An unknown optional header magic returns an error.
    [Test]
    public void GetDataDirectory_AnUnknownMagic_ReturnsAnError()
    {
        var image = PeImage.Create(true, _sorted);
        image.Bytes[image.File.DosHeader.NextHeaderOffset + MagicOffset] = 0x07;
        var (memory, module) = Load(image);

        Assert.That(memory.GetDataDirectory(module, PeDirectory.Export).IsErr, Is.True);
    }

    // An NT header offset past the image returns an error instead of reading outside the module.
    [Test]
    public void GetDataDirectory_AnNtOffsetPastTheImage_ReturnsAnError()
    {
        var image = PeImage.Create(true, _sorted);
        BinaryPrimitives.WriteInt32LittleEndian(image.Bytes.AsSpan(LfanewOffset), image.Bytes.Length);
        var (memory, module) = Load(image);

        Assert.That(memory.GetDataDirectory(module, PeDirectory.Export).IsErr, Is.True);
    }

    // GetDataDirectory reads the entries the PE file was written with, in PE32 and PE32+ images.
    [Test]
    public void GetDataDirectory_BothFormats_MatchesTheWrittenEntries([Values] bool is64Bit)
    {
        var image = PeImage.Create(is64Bit, _sorted);
        var (memory, module) = Load(image);

        var export = image.File.OptionalHeader.GetDataDirectory(DataDirectoryIndex.ExportDirectory);
        var debug = image.File.OptionalHeader.GetDataDirectory(DataDirectoryIndex.DebugDirectory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                memory.GetDataDirectory(module, PeDirectory.Export).Unwrap(),
                Is.EqualTo(new PeDataDirectory(export.VirtualAddress, export.Size)));
            Assert.That(
                memory.GetDataDirectory(module, PeDirectory.Debug).Unwrap(),
                Is.EqualTo(new PeDataDirectory(debug.VirtualAddress, debug.Size)));
        }
    }

    // A directory past the image's directory count reads as empty.
    [Test]
    public void GetDataDirectory_PastTheDirectoryCount_ReturnsAnEmptyEntry()
    {
        var image = PeImage.Create(true, _sorted, configure: file => file.OptionalHeader.NumberOfRvaAndSizes = 1);
        var (memory, module) = Load(image);

        Assert.That(memory.GetDataDirectory(module, PeDirectory.Debug).Unwrap(), Is.Default);
    }

#if NET
    // A found export doesn't allocate anything once the code has run once.
    [Test]
    public void GetExport_AFoundName_DoesNotAllocate()
    {
        var (memory, module) = Load(PeImage.Create(true, _sorted));
        _ = memory.GetExport(module, "Gamma");

        long before = GC.GetAllocatedBytesForCurrentThread();
        var export = memory.GetExport(module, "Gamma");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.IsOk, Is.True);
            Assert.That(allocated, Is.Zero);
        }
    }
#endif

    private static (BufferMemory Memory, MemoryModule Module) Load(PeImage image)
    {
        BufferMemory memory = new(_base, image.Bytes, MemoryLayout.X64);
        MemoryModule module = new("test.dll", null, memory.Range);

        return (memory, module);
    }
}
