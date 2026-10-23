using NUnit.Framework;

namespace AslHelp.Memory.Tests;

public partial class MemoryReaderExtensionsTests
{
    private static readonly MemoryLayout _bigEndian24 = new(3, ByteOrder.BigEndian);

    // A 3-byte big-endian pointer is read with its highest byte first and zero-extended.
    [Test]
    public void ReadPointer_ThreeBytesBigEndian_ZeroExtends()
    {
        BufferMemory memory = new(Base, [0x80, 0x10, 0x20, 0xFF], _bigEndian24);

        Assert.That(memory.ReadPointer(new Address(Base)).Unwrap(), Is.EqualTo(new Address(0x801020)));
    }

    // A 3-byte little-endian pointer is read with its lowest byte first.
    [Test]
    public void ReadPointer_ThreeBytesLittleEndian_ReadsLowByteFirst()
    {
        BufferMemory memory = new(Base, [0x20, 0x10, 0x80], new MemoryLayout(3, ByteOrder.LittleEndian));

        Assert.That(memory.ReadPointer(new Address(Base)).Unwrap(), Is.EqualTo(new Address(0x801020)));
    }

    // An 8-byte pointer is read whole.
    [Test]
    public void ReadPointer_EightBytes_ReadsAllOfThem()
    {
        BufferMemory memory = new(Base, [0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x81], MemoryLayout.X64);

        Assert.That(memory.ReadPointer(new Address(Base)).Unwrap(), Is.EqualTo(new Address(0x8102030405060708)));
    }

    // A default layout has a pointer size of 0, which ReadPointer refuses.
    [Test]
    public void ReadPointer_DefaultLayout_ReturnsAnError()
    {
        BufferMemory memory = new(Base, new byte[8], default);

        Assert.That(memory.ReadPointer(new Address(Base)).IsErr, Is.True);
    }

    // Deref reads a pointer at each step and adds the next offset to it.
    [Test]
    public void Deref_ThreeBytePointers_FollowsTheChain()
    {
        BufferMemory memory = new(Base,
        [
            0x00, 0x10, 0x04,
            0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x10, 0x02,
        ], _bigEndian24);

        Assert.That(memory.Deref(new Address(Base), 6, 1).Unwrap(), Is.EqualTo(new Address(0x1003)));
    }

    // Deref without offsets returns the start address without reading.
    [Test]
    public void Deref_NoOffsets_ReturnsTheStart()
    {
        BufferMemory memory = new(Base, [], MemoryLayout.X64);

        Assert.That(memory.Deref(new Address(0x1234)).Unwrap(), Is.EqualTo(new Address(0x1234)));
    }

    // Deref stops with an error at a pointer that isn't readable.
    [Test]
    public void Deref_AnUnreadableStep_ReturnsAnError()
    {
        BufferMemory memory = new(Base, [0xFF, 0xFF, 0xFF, 0x00], MemoryLayout.X86);

        Assert.That(memory.Deref(new Address(Base), 0, 0).IsErr, Is.True);
    }
}
