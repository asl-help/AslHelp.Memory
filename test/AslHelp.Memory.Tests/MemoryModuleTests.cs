using NUnit.Framework;
using System.IO;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class MemoryModuleTests
{
    // SHA256 of the 3 bytes "abc".
    private const string AbcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    // SHA256 of an empty file.
    private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

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

    // The hash is the SHA256 of the file as 64 lowercase hex digits.
    [Test]
    public void GetFileHash_AFile_ReturnsItsSha256()
    {
        MemoryModule module = new("file.bin", WriteFile("file.bin", "abc"), _range);

        Assert.That(module.GetFileHash().Unwrap(), Is.EqualTo(AbcHash));
    }

    // The 1st hash is kept, so a file changed on disk later doesn't change it.
    [Test]
    public void GetFileHash_CalledAgainAfterTheFileChanged_ReturnsTheFirstHash()
    {
        string path = WriteFile("file.bin", "abc");
        MemoryModule module = new("file.bin", path, _range);

        _ = module.GetFileHash();
        File.WriteAllText(path, "other");

        Assert.That(module.GetFileHash().Unwrap(), Is.EqualTo(AbcHash));
    }

    // A file another handle keeps open for writing is still hashed, as a running game keeps its files open.
    [Test]
    public void GetFileHash_AFileOpenForWriting_StillHashes()
    {
        string path = WriteFile("file.bin", "abc");
        MemoryModule module = new("file.bin", path, _range);

        using FileStream writer = new(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        Assert.That(module.GetFileHash().Unwrap(), Is.EqualTo(AbcHash));
    }

    // A missing file returns an error with its path.
    [Test]
    public void GetFileHash_MissingFile_ReturnsAnErrorWithThePath()
    {
        string path = Path.Combine(_folder, "missing.bin");
        MemoryModule module = new("missing.bin", path, _range);

        Assert.That(module.GetFileHash().UnwrapErr().Message, Does.Contain(path));
    }

    // A failure isn't kept, so the file is hashed once it exists.
    [Test]
    public void GetFileHash_AfterAFailure_HashesOnceTheFileExists()
    {
        MemoryModule module = new("file.bin", Path.Combine(_folder, "file.bin"), _range);

        _ = module.GetFileHash();
        _ = WriteFile("file.bin", "abc");

        Assert.That(module.GetFileHash().Unwrap(), Is.EqualTo(AbcHash));
    }

    // A module without a path returns an error with its name.
    [Test]
    public void GetFileHash_NoPath_ReturnsAnErrorWithTheName()
    {
        MemoryModule module = new("file.bin", null, _range);

        Assert.That(module.GetFileHash().UnwrapErr().Message, Does.Contain("'file.bin'"));
    }

    // The kept hash isn't part of equality, so a hashed module still equals an equal one that wasn't hashed.
    [Test]
    public void Equals_OneModuleHashed_StillEqualsAnUnhashedModule()
    {
        string path = WriteFile("file.bin", "abc");
        MemoryModule hashed = new("file.bin", path, _range);
        MemoryModule other = new("file.bin", path, _range);

        _ = hashed.GetFileHash();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashed, Is.EqualTo(other));
            Assert.That(hashed.GetHashCode(), Is.EqualTo(other.GetHashCode()));
        }
    }

    // An equal module keeps its own hash, so it reads the file again instead of reusing the hash of the 1st one.
    [Test]
    public void GetFileHash_AnEqualModuleAfterTheFileChanged_HashesTheNewFile()
    {
        string path = WriteFile("file.bin", "abc");
        MemoryModule hashed = new("file.bin", path, _range);
        MemoryModule other = new("file.bin", path, _range);

        _ = hashed.GetFileHash();
        File.WriteAllText(path, "");

        Assert.That(other.GetFileHash().Unwrap(), Is.EqualTo(EmptyHash));
    }

    // A copy made with another path hashes its own file instead of keeping the hash of the original.
    [Test]
    public void GetFileHash_ACopyWithAnotherPath_HashesTheOtherFile()
    {
        MemoryModule hashed = new("file.bin", WriteFile("file.bin", "abc"), _range);
        _ = hashed.GetFileHash();

        var copy = hashed with { Path = WriteFile("other.bin", "") };

        Assert.That(copy.GetFileHash().Unwrap(), Is.EqualTo(EmptyHash));
    }

    private string WriteFile(string name, string text)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);

        return path;
    }
}
