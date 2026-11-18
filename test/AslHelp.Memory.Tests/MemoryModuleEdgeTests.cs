using NUnit.Framework;
using System.IO;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class MemoryModuleEdgeTests
{
    // SHA256 of the 3 bytes "abc".
    private const string AbcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static readonly MemoryRange _range = new(new Address(0x1000), 0x100);

    private string _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _ = Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_folder, true);
    }

    // A path with a non-ASCII folder and file name is hashed like an ASCII path.
    [Test]
    public void GetFileHash_UnicodePath_ReturnsItsSha256()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_folder, "spieler ü 日本")).FullName;
        string path = Path.Combine(folder, "datei é.bin");
        File.WriteAllText(path, "abc");

        MemoryModule module = new("datei é.bin", path, _range);

        Assert.That(module.GetFileHash().Unwrap(), Is.EqualTo(AbcHash));
    }

    // A path to a folder returns an error with the path instead of throwing.
    [Test]
    public void GetFileHash_PathOfAFolder_ReturnsAnErrorWithThePath()
    {
        MemoryModule module = new("file.bin", _folder, _range);

        Assert.That(module.GetFileHash().UnwrapErr().Message, Does.Contain(_folder));
    }

    // A file another handle opened without sharing returns an error instead of throwing.
    [Test]
    public void GetFileHash_FileOpenWithoutSharing_ReturnsAnError()
    {
        string path = Path.Combine(_folder, "file.bin");
        File.WriteAllText(path, "abc");
        MemoryModule module = new("file.bin", path, _range);

        using FileStream locker = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.That(module.GetFileHash().UnwrapErr().Message, Does.Contain(path));
    }

    // An empty or malformed path returns an error on both frameworks instead of throwing.
    [TestCase("")]
    [TestCase("file|name.bin")]
    [TestCase("file\0name.bin")]
    [TestCase("C:\\folder\\file:name:bin")]
    public void GetFileHash_InvalidPath_ReturnsAnError(string path)
    {
        MemoryModule module = new("file.bin", path, _range);

        Assert.That(module.GetFileHash().IsErr, Is.True);
    }

    // Equals compares every positional value, so modules that differ in 1 of them aren't equal.
    [TestCase("other.bin", "C:/file.bin", 0x1000UL, 0x100UL)]
    [TestCase("file.bin", "C:/other.bin", 0x1000UL, 0x100UL)]
    [TestCase("file.bin", null, 0x1000UL, 0x100UL)]
    [TestCase("file.bin", "C:/file.bin", 0x2000UL, 0x100UL)]
    [TestCase("file.bin", "C:/file.bin", 0x1000UL, 0x200UL)]
    public void Equals_OneValueDiffers_ReturnsFalse(string name, string? path, ulong start, ulong size)
    {
        MemoryModule module = new("file.bin", "C:/file.bin", _range);
        MemoryModule other = new(name, path, new(new Address(start), size));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(module, Is.Not.EqualTo(other));
            Assert.That(other, Is.Not.EqualTo(module));
        }
    }
}
