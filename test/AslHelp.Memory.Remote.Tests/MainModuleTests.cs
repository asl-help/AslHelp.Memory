using NUnit.Framework;
using System.IO;
using System.Runtime.Versioning;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MainModuleTests
{
    private FixtureChild _child = null!;

    [TearDown]
    public void TearDown()
    {
        // Skips what the test never started, because an inconclusive start leaves the field unset.
        _child?.Dispose();
    }

    // The main module of a 64-bit child is the exe it was started from and contains its Tick export.
    [Test]
    public void GetMainModule_Child64Bit_ReturnsTheExecutable()
    {
        _child = FixtureChild.Start();

        AssertIsTheExecutable(_child);
    }

    // The main module of a 32-bit child is the exe it was started from, read from the WOW64 module list.
    [Test]
    public void GetMainModule_Child32Bit_ReturnsTheExecutable()
    {
        _child = FixtureChild.Start32Bit();

        AssertIsTheExecutable(_child);
    }

    private static void AssertIsTheExecutable(FixtureChild child)
    {
        // Normalizes the slashes, because the start path comes from MSBuild with mixed ones.
        string path = Path.GetFullPath(child.Process.StartInfo.FileName);

        var module = child.Memory.GetMainModule().Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(module.Path, Is.EqualTo(path).IgnoreCase);
            Assert.That(module.Name, Is.EqualTo(Path.GetFileName(path)).IgnoreCase);
            Assert.That(module.Range.Contains(child.Tick), Is.True);
            Assert.That(child.Memory.GetModules().Unwrap(), Has.Some.Property(nameof(MemoryModule.Range)).EqualTo(module.Range));
        }
    }
}
