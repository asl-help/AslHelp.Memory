using NUnit.Framework;

namespace AslHelp.Memory.Tests;

public partial class MemoryReaderExtensionsTests
{
    // ReadString decodes ASCII up to the first terminator inside the length.
    [Test]
    public void ReadString_Ascii_CutsAtTheTerminator()
    {
        BufferMemory memory = new(Base, [(byte)'a', (byte)'b', 0, (byte)'c'], MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), 4, StringType.Ascii).Unwrap(), Is.EqualTo("ab"));
    }

    // ReadString decodes UTF-8 across multi-byte characters.
    [Test]
    public void ReadString_Utf8_DecodesMultiByteCharacters()
    {
        BufferMemory memory = new(Base, [0xC3, 0xA9, (byte)'e'], MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), 3, StringType.Utf8).Unwrap(), Is.EqualTo("ée"));
    }

    // UTF-16 follows the layout's byte order.
    [Test]
    public void ReadString_Utf16_FollowsTheByteOrder()
    {
        BufferMemory little = new(Base, [(byte)'h', 0, (byte)'i', 0], MemoryLayout.X86);
        BufferMemory big = new(Base, [0, (byte)'h', 0, (byte)'i'], MemoryLayout.BigEndian32);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(little.ReadString(new Address(Base), 2, StringType.Utf16).Unwrap(), Is.EqualTo("hi"));
            Assert.That(big.ReadString(new Address(Base), 2, StringType.Utf16).Unwrap(), Is.EqualTo("hi"));
        }
    }

    // ReadTerminatedString stops at the terminator.
    [Test]
    public void ReadTerminatedString_Utf16BigEndian_StopsAtTheTerminator()
    {
        BufferMemory memory = new(Base, [0, (byte)'o', 0, (byte)'k', 0, 0], MemoryLayout.BigEndian32);

        Assert.That(memory.ReadTerminatedString(new Address(Base), StringType.Utf16, 3).Unwrap(), Is.EqualTo("ok"));
    }

    // ReadTerminatedString returns an error when no terminator comes within the bound.
    [Test]
    public void ReadTerminatedString_NoTerminatorWithinTheBound_ReturnsAnError()
    {
        BufferMemory memory = new(Base, [(byte)'a', (byte)'b', (byte)'c', (byte)'d', 0], MemoryLayout.X86);

        Assert.That(memory.ReadTerminatedString(new Address(Base), StringType.Ascii, 3).IsErr, Is.True);
    }

    // ReadTerminatedString reads a string that ends close to the end of readable memory.
    [Test]
    public void ReadTerminatedString_NearTheEnd_ReadsTheString()
    {
        BufferMemory memory = new(Base, [(byte)'x', 0], MemoryLayout.X86);

        Assert.That(memory.ReadTerminatedString(new Address(Base), StringType.Utf8).Unwrap(), Is.EqualTo("x"));
    }
}
