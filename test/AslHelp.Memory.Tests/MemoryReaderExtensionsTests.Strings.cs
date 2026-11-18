using NUnit.Framework;

namespace AslHelp.Memory.Tests;

public partial class MemoryReaderExtensionsTests
{
    // ReadString decodes ASCII up to the first terminator inside the length.
    [Test]
    public void ReadString_Ascii_CutsAtTheTerminator()
    {
        BufferMemory memory = new(new Address(Base), [(byte)'a', (byte)'b', 0, (byte)'c'], MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), 4, StringType.Ascii).Unwrap(), Is.EqualTo("ab"));
    }

    // ReadString decodes UTF-8 across multi-byte characters.
    [Test]
    public void ReadString_Utf8_DecodesMultiByteCharacters()
    {
        BufferMemory memory = new(new Address(Base), [0xC3, 0xA9, (byte)'e'], MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), 3, StringType.Utf8).Unwrap(), Is.EqualTo("ée"));
    }

    // UTF-16 follows the layout's byte order.
    [Test]
    public void ReadString_Utf16_FollowsTheByteOrder()
    {
        BufferMemory little = new(new Address(Base), [(byte)'h', 0, (byte)'i', 0], MemoryLayout.X86);
        BufferMemory big = new(new Address(Base), [0, (byte)'h', 0, (byte)'i'], MemoryLayout.BigEndian32);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(little.ReadString(new Address(Base), 2, StringType.Utf16).Unwrap(), Is.EqualTo("hi"));
            Assert.That(big.ReadString(new Address(Base), 2, StringType.Utf16).Unwrap(), Is.EqualTo("hi"));
        }
    }

    // ReadString stops at the terminator.
    [Test]
    public void ReadString_Utf16BigEndian_StopsAtTheTerminator()
    {
        BufferMemory memory = new(new Address(Base), [0, (byte)'o', 0, (byte)'k', 0, 0], MemoryLayout.BigEndian32);

        Assert.That(memory.ReadString(new Address(Base), StringType.Utf16, 3).Unwrap(), Is.EqualTo("ok"));
    }

    // ReadString returns an error when no terminator comes within the bound.
    [Test]
    public void ReadString_NoTerminatorWithinTheBound_ReturnsAnError()
    {
        BufferMemory memory = new(new Address(Base), [(byte)'a', (byte)'b', (byte)'c', (byte)'d', 0], MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), StringType.Ascii, 3).IsErr, Is.True);
    }

    // ReadString reads a string that ends close to the end of readable memory.
    [Test]
    public void ReadString_NearTheEnd_ReadsTheString()
    {
        BufferMemory memory = new(new Address(Base), [(byte)'x', 0], MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), StringType.Utf8).Unwrap(), Is.EqualTo("x"));
    }

    // A UTF-16 string longer than the stack space reads through pooled space.
    [Test]
    public void ReadString_LongUtf16_ReadsTheString()
    {
        string text = new('z', 2000);
        byte[] bytes = new byte[(text.Length + 1) * 2];
        System.Text.Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
        BufferMemory memory = new(new Address(Base), bytes, MemoryLayout.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(memory.ReadString(new Address(Base), text.Length, StringType.Utf16).Unwrap(), Is.EqualTo(text));
            Assert.That(memory.ReadString(new Address(Base), StringType.Utf16, 4000).Unwrap(), Is.EqualTo(text));
        }
    }

    // A non-zero 2nd byte reads as UTF-8.
    [Test]
    public void ReadString_AutoAsciiLooking_ReadsUtf8()
    {
        BufferMemory memory = new(new Address(Base), [(byte)'h', 0xC3, 0xA9, 0], MemoryLayout.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(memory.ReadString(new Address(Base), 4, StringType.Auto).Unwrap(), Is.EqualTo("hé"));
            Assert.That(memory.ReadString(new Address(Base), StringType.Auto).Unwrap(), Is.EqualTo("hé"));
        }
    }

    // 2 chars with a 0 high byte read as UTF-16 with the length counted in chars, but a read up to a terminator reads
    // UTF-8.
    [Test]
    public void ReadString_AutoUtf16Looking_ReadsUtf16WithALength()
    {
        BufferMemory memory = new(new Address(Base), [(byte)'h', 0, (byte)'i', 0, 0, 0], MemoryLayout.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(memory.ReadString(new Address(Base), 3, StringType.Auto).Unwrap(), Is.EqualTo("hi"));
            Assert.That(memory.ReadString(new Address(Base), StringType.Auto).Unwrap(), Is.EqualTo("h"));
        }
    }

    // A 1-char string reads as UTF-8, because its 2nd char is a terminator and not a Latin char.
    [Test]
    public void ReadString_AutoOneChar_ReadsTheChar()
    {
        BufferMemory utf8 = new(new Address(Base), [(byte)'x'], MemoryLayout.X86);
        BufferMemory utf16 = new(new Address(Base), [(byte)'x', 0, 0, 0], MemoryLayout.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(utf8.ReadString(new Address(Base), 1, StringType.Auto).Unwrap(), Is.EqualTo("x"));
            Assert.That(utf16.ReadString(new Address(Base), 2, StringType.Auto).Unwrap(), Is.EqualTo("x"));
            Assert.That(utf16.ReadString(new Address(Base), StringType.Auto).Unwrap(), Is.EqualTo("x"));
        }
    }

    // An empty string reads as empty, with or without a length.
    [Test]
    public void ReadString_AutoEmpty_IsEmpty()
    {
        BufferMemory memory = new(new Address(Base), [0, 0, 0, 0], MemoryLayout.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(memory.ReadString(new Address(Base), 0, StringType.Auto).Unwrap(), Is.Empty);
            Assert.That(memory.ReadString(new Address(Base), 2, StringType.Auto).Unwrap(), Is.Empty);
            Assert.That(memory.ReadString(new Address(Base), StringType.Auto, 0).Unwrap(), Is.Empty);
        }
    }

    // An odd length reads the 2nd half of a UTF-16 string after the detection.
    [Test]
    public void ReadString_AutoOddLength_ReadsEveryUnit()
    {
        BufferMemory utf8 = new(new Address(Base), [(byte)'a', (byte)'b', (byte)'c'], MemoryLayout.X86);
        BufferMemory utf16 = new(new Address(Base), [(byte)'a', 0, (byte)'b', 0, (byte)'c', 0], MemoryLayout.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(utf8.ReadString(new Address(Base), 3, StringType.Auto).Unwrap(), Is.EqualTo("abc"));
            Assert.That(utf16.ReadString(new Address(Base), 3, StringType.Auto).Unwrap(), Is.EqualTo("abc"));
            Assert.That(utf16.ReadString(new Address(Base), StringType.Auto, 2).Unwrap(), Is.EqualTo("a"));
        }
    }

    // A string at the end of readable memory reads without a read past it.
    [Test]
    public void ReadString_AutoAtTheEnd_ReadsTheString()
    {
        BufferMemory utf8 = new(new Address(Base), [(byte)'h', (byte)'i', 0], MemoryLayout.X86);
        BufferMemory utf16 = new(new Address(Base), [(byte)'h', 0, (byte)'i', 0, 0, 0], MemoryLayout.X86);
        BufferMemory empty = new(new Address(Base), [0], MemoryLayout.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(utf8.ReadString(new Address(Base), 3, StringType.Auto).Unwrap(), Is.EqualTo("hi"));
            Assert.That(utf8.ReadString(new Address(Base), StringType.Auto).Unwrap(), Is.EqualTo("hi"));
            Assert.That(utf16.ReadString(new Address(Base), StringType.Auto).Unwrap(), Is.EqualTo("h"));
            Assert.That(empty.ReadString(new Address(Base), StringType.Auto).Unwrap(), Is.Empty);
        }
    }

    // A UTF-16 string that runs past readable memory fails on the read of its 2nd half.
    [Test]
    public void ReadString_AutoUtf16PastTheEnd_ReturnsAnError()
    {
        BufferMemory memory = new(new Address(Base), [(byte)'h', 0, (byte)'i', 0, (byte)'!'], MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), 3, StringType.Auto).IsErr, Is.True);
    }

    // Fewer than 4 readable bytes read as UTF-8 within the length.
    [Test]
    public void ReadString_AutoFewerThan4Bytes_ReadsUtf8()
    {
        BufferMemory memory = new(new Address(Base), [(byte)'h', 0, (byte)'i'], MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), 3, StringType.Auto).Unwrap(), Is.EqualTo("h"));
    }

    // The guess looks at the high bytes of the first 2 chars in the layout's byte order.
    [Test]
    public void ReadString_AutoBigEndian_FollowsTheByteOrder()
    {
        BufferMemory utf16 = new(new Address(Base), [0, (byte)'h', 0, (byte)'i', 0, 0], MemoryLayout.BigEndian32);
        BufferMemory utf8 = new(new Address(Base), [(byte)'h', 0, (byte)'i', 0], MemoryLayout.BigEndian32);
        BufferMemory nonLatin = new(new Address(Base), [0, (byte)'A', 0x30, 0x42], MemoryLayout.BigEndian32);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(utf16.ReadString(new Address(Base), 3, StringType.Auto).Unwrap(), Is.EqualTo("hi"));
            Assert.That(utf16.ReadString(new Address(Base), StringType.Auto).Unwrap(), Is.Empty);
            Assert.That(utf8.ReadString(new Address(Base), 4, StringType.Auto).Unwrap(), Is.EqualTo("h"));
            Assert.That(utf8.ReadString(new Address(Base), StringType.Auto).Unwrap(), Is.EqualTo("h"));
            Assert.That(nonLatin.ReadString(new Address(Base), 2, StringType.Auto).Unwrap(), Is.Empty);
        }
    }

    // Only a [x, 0, y, 0] start with non-zero x and y reads as UTF-16.
    [TestCase(new byte[] { 0x41, 0x00, 0x42, 0x00, 0x00, 0x00 }, 3, "AB")]
    [TestCase(new byte[] { 0x41, 0x00, 0x42, 0x00 }, 2, "AB")]
    [TestCase(new byte[] { 0x41, 0x42, 0x00, 0x43 }, 4, "AB")]
    [TestCase(new byte[] { 0x00, 0x41, 0x42, 0x00 }, 4, "")]
    [TestCase(new byte[] { 0x42, 0x30, 0x00, 0x00 }, 4, "B0")]
    [TestCase(new byte[] { 0x41, 0x00, 0x00, 0x00 }, 2, "A")]
    [TestCase(new byte[] { 0x41, 0x00, 0x42, 0x30 }, 2, "A")]
    [TestCase(new byte[] { 0x41 }, 1, "A")]
    public void ReadString_Auto_PicksUtf16OnlyForLatinUtf16(byte[] bytes, int length, string expected)
    {
        BufferMemory memory = new(new Address(Base), bytes, MemoryLayout.X86);

        Assert.That(memory.ReadString(new Address(Base), length, StringType.Auto).Unwrap(), Is.EqualTo(expected));
    }
}
