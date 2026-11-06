using AslHelp.Memory.Remote.Errors;
using NUnit.Framework;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class SRTests
{
    private static readonly Address _anchor = new(0x1000);

    // A property reads its text from the embedded resources.
    [Test]
    public void Property_AMessage_ReadsItsText()
    {
        Assert.That(SR.PatchCannotSuspendItself, Is.EqualTo("A process cannot suspend its own threads."));
    }

    // An Address argument writes its own 0x prefix.
    [Test]
    public void Message_AnAddress_WritesItsPrefix()
    {
        Assert.That(
            RemotePatchError.OutOfReach(_anchor).Message,
            Is.EqualTo("No memory within reach of 0x1000 was free, so nothing there can be hooked."));
    }

    // A failed read of a real process formats its message when something reads it.
    [Test]
    public void Read_AnUnmappedAddress_HasTheText()
    {
        using var process = Process.GetCurrentProcess();
        using RemoteProcessMemory memory = new(process);

        Assert.That(memory.Read<int>(new Address(0x10)).Error!.Message, Is.EqualTo("Couldn't read 4 bytes at 0x10."));
    }
}
