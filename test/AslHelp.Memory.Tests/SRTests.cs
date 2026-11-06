using AslHelp.Memory.Errors;
using AslHelp.Memory.Patching.Errors;
using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class SRTests
{
    private const int FailedReads = 1000;

    // A read fails with an object and formats nothing, so this stays well under the shortest message.
    private const int MaxBytesPerFailedRead = 64;

    private static readonly Address _base = new(0x1000);
    private static readonly Address _missing = new(0x10);

    // A property reads its text from the embedded resources.
    [Test]
    public void Property_AMessage_ReadsItsText()
    {
        Assert.That(SR.CaptureNoRegisters, Is.EqualTo("A 32-bit target passes every argument on the stack, so there's no register to capture."));
    }

    // Every message in the resx loads at run time.
    [Test]
    public void Property_EveryMessage_LoadsText()
    {
        var properties = typeof(SR)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(properties, Is.Not.Empty);

            foreach (var property in properties)
            {
                Assert.That(property.GetValue(null), Is.Not.Null.And.Not.Empty, property.Name);
            }
        }
    }

    // A format method fills every placeholder.
    [Test]
    public void Message_AnErrorWithValues_FormatsItsText()
    {
        Assert.That(
            MemoryError.ModuleNotFound("game.dll").Message,
            Is.EqualTo("No module named 'game.dll' is loaded."));
    }

    // A placeholder after 0x gets its digits as text, because the generator counts only plain placeholders.
    [Test]
    public void Message_AHexValue_WritesItsDigits()
    {
        Assert.That(
            CaptureError.OverlappingCaptures(0x30, 0x34).Message,
            Is.EqualTo("The captures at 0x30 and 0x34 overlap, so 1 would be read as the other."));
    }

    // Braces and angle brackets in a message survive the resx unformatted.
    [Test]
    public void Property_AMessageWithBraces_KeepsThem()
    {
        Assert.That(
            SR.LocatorHasSteps,
            Is.EqualTo("""A locator has named routes, not steps. Write it as { "<route name>": [ <steps> ] }."""));
    }

    // A thrown exception reads its text from the resources too.
    [Test]
    public void Parse_NotAnAddress_ThrowsWithTheText()
    {
        Assert.That(
            () => Address.Parse("text"),
            Throws.InstanceOf<FormatException>().With.Message.EqualTo("'text' isn't an address. An address is 0x followed by hex or a decimal number."));
    }

    // A failed read formats its message when something reads it.
    [Test]
    public void Read_OutsideTheMemory_HasTheText()
    {
        BufferMemory memory = new(_base, new byte[4], MemoryLayout.X64);

        Assert.That(memory.Read(_missing, new byte[4]).Error!.Message, Is.EqualTo("Couldn't read 4 bytes at 0x10."));
    }

    // A failed write has its own text.
    [Test]
    public void Write_OutsideTheMemory_HasTheText()
    {
        BufferMemory memory = new(_base, new byte[4], MemoryLayout.X64);

        Assert.That(memory.Write(_missing, new byte[4]).Error!.Message, Is.EqualTo("Couldn't write 4 bytes at 0x10."));
    }

    // A string read that runs out of its bound has the text with the rewritten wording.
    [Test]
    public void ReadString_NoTerminator_HasTheText()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("abcdefgh");
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        Assert.That(
            memory.ReadString(_base, StringType.Ascii, 3).Error!.Message,
            Is.EqualTo("The string at 0x1000 doesn't have a terminator within 3 characters."));
    }

    // A host-sized read has the type's name in its text.
    [Test]
    public void Read_AHostSizedType_HasTheText()
    {
        BufferMemory memory = new(_base, new byte[8], MemoryLayout.X64);

        Assert.That(
            memory.Read<nint>(_base).Error!.Message,
            Is.EqualTo("'IntPtr' is as wide as a pointer on the host, not in the target. Use ReadPointer instead."));
    }

    // 2 failed reads of the same bytes compare equal, as they did when the message was the value.
    [Test]
    public void Read_SameBytesTwice_ComparesEqual()
    {
        BufferMemory memory = new(_base, new byte[4], MemoryLayout.X64);

        var first = memory.Read(_missing, new byte[4]).Error;
        var second = memory.Read(_missing, new byte[4]).Error;

        Assert.That(first, Is.EqualTo(second));
    }

    // The neutral text is the fallback under a UI culture without its own resources.
    [Test]
    [SetUICulture("de-DE")]
    public void Message_AnotherUiCulture_FallsBackToTheNeutralText()
    {
        Assert.That(MemoryError.ModuleNotFound("game.dll").Message, Is.EqualTo("No module named 'game.dll' is loaded."));
    }

    // A script's reads run per tick and fail often, so a failed read must not format its message.
    [Test]
    public void Read_ManyFailures_AllocatesNoMessage()
    {
        BufferMemory memory = new(_base, new byte[4], MemoryLayout.X64);
        long failures = FailRepeatedly(memory);

        long before = GC.GetAllocatedBytesForCurrentThread();
        failures += FailRepeatedly(memory);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failures, Is.EqualTo(2 * FailedReads));
            Assert.That(allocated / FailedReads, Is.LessThanOrEqualTo(MaxBytesPerFailedRead));
        }
    }

    private static long FailRepeatedly(BufferMemory memory)
    {
        long failures = 0;
        for (int i = 0; i < FailedReads; i++)
        {
            failures += memory.Read<int>(_missing).IsErr ? 1 : 0;
        }

        return failures;
    }
}
