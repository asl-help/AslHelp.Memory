using NUnit.Framework;
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class MainModuleEdgeTests
{
    private FixtureChild _child = null!;

    [TearDown]
    public void TearDown()
    {
        // Skips what the test never started, because an inconclusive start leaves the field unset.
        _child?.Dispose();
    }

    // The exe of a running child is still hashed, though Windows keeps the image file open while it runs.
    [Test]
    public void GetFileHash_MainModuleOfARunningChild_ReturnsTheHashOfTheExe()
    {
        _child = FixtureChild.Start();

        var module = _child.Memory.GetMainModule().Unwrap();

        Assert.That(module.GetFileHash().Unwrap(), Is.EqualTo(HashFile(module.Path!)));
    }

    // A child that already exited returns an error instead of throwing or returning a stale module.
    [Test]
    public void GetMainModule_ExitedChild_ReturnsAnError()
    {
        _child = FixtureChild.Start();
        _child.Process.StandardInput.Close();
        _child.Process.WaitForExit();

        Assert.That(_child.Memory.GetMainModule().IsErr, Is.True);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha256 = SHA256.Create();

        return Convert.ToHexStringLower(sha256.ComputeHash(stream));
    }
}
