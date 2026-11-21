using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class Wow64ModuleTests
{
    // A 32-bit process on 64-bit Windows loads its system modules from SysWOW64, so its kernel32 path points there.
    [Test]
    public void GetModules_Child32Bit_GivesSysWow64Paths()
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            Assert.Ignore("Windows is 32-bit, so it has no SysWOW64 folder.");
        }

        using var child = FixtureChild.Start32Bit();
        string sysWow64 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);

        var kernel32 = child.Memory.GetModules().Unwrap()
            .Where(module => module.Name.Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase))
            .Single(module => module.Range.Start.Value < uint.MaxValue);

        Assert.That(Path.GetDirectoryName(kernel32.Path), Is.EqualTo(sysWow64).IgnoreCase);
    }
}
