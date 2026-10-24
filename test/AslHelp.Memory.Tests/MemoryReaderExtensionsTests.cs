using NUnit.Framework;

namespace AslHelp.Memory.Tests;

[TestFixture]
public partial class MemoryReaderExtensionsTests
{
    private const ulong Base = 0x1000;

    // A little-endian int is read with its lowest byte first.
    [Test]
    public void Read_IntLittleEndian_ReadsLowByteFirst()
    {
        BufferMemory memory = new(new Address(Base), [0x78, 0x56, 0x34, 0x12], MemoryLayout.X86);

        Assert.That(memory.Read<int>(new Address(Base)).Unwrap(), Is.EqualTo(0x12345678));
    }

    // A big-endian int is read with its highest byte first.
    [Test]
    public void Read_IntBigEndian_ReadsHighByteFirst()
    {
        BufferMemory memory = new(new Address(Base), [0x12, 0x34, 0x56, 0x78], MemoryLayout.BigEndian32);

        Assert.That(memory.Read<int>(new Address(Base)).Unwrap(), Is.EqualTo(0x12345678));
    }

    // A float is read in the layout's byte order.
    [Test]
    public void Read_FloatInBothByteOrders_ReadsTheSameValue()
    {
        BufferMemory little = new(new Address(Base), [0x00, 0x00, 0x80, 0x3F], MemoryLayout.X86);
        BufferMemory big = new(new Address(Base), [0x3F, 0x80, 0x00, 0x00], MemoryLayout.BigEndian32);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(little.Read<float>(new Address(Base)).Unwrap(), Is.EqualTo(1f));
            Assert.That(big.Read<float>(new Address(Base)).Unwrap(), Is.EqualTo(1f));
        }
    }

    // A big-endian double is read with its sign and exponent byte first.
    [Test]
    public void Read_DoubleBigEndian_ReadsHighByteFirst()
    {
        BufferMemory memory = new(new Address(Base), [0x40, 0x00, 0, 0, 0, 0, 0, 0], MemoryLayout.BigEndian32);

        Assert.That(memory.Read<double>(new Address(Base)).Unwrap(), Is.EqualTo(2d));
    }

    // A read past the end of readable memory returns an error instead of throwing.
    [Test]
    public void Read_PastTheEnd_ReturnsAnError()
    {
        BufferMemory memory = new(new Address(Base), [1, 2], MemoryLayout.X86);

        Assert.That(memory.Read<int>(new Address(Base)).IsErr, Is.True);
    }

    // A read below the start of readable memory returns an error.
    [Test]
    public void Read_BelowTheStart_ReturnsAnError()
    {
        BufferMemory memory = new(new Address(Base), [1, 2, 3, 4], MemoryLayout.X86);

        Assert.That(memory.Read<int>(new Address(Base - 1)).IsErr, Is.True);
    }

    // A host-sized type is refused because its width depends on the host and not on the layout.
    [Test]
    public void Read_HostSizedTypes_ReturnAnError()
    {
        BufferMemory memory = new(new Address(Base), new byte[8], MemoryLayout.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(memory.Read<nint>(new Address(Base)).IsErr, Is.True);
            Assert.That(memory.Read<nuint>(new Address(Base)).IsErr, Is.True);
            Assert.That(memory.ReadArray<nint>(new Address(Base), 1).IsErr, Is.True);
        }
    }

    // A struct is read as raw bytes without the byte order applied.
    [Test]
    public void Read_AStructBigEndian_ReadsRawBytes()
    {
        BufferMemory memory = new(new Address(Base), [0x01, 0x02, 0x03, 0x04], MemoryLayout.BigEndian32);

        var pair = memory.Read<Pair>(new Address(Base)).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pair.First, Is.EqualTo(0x0201));
            Assert.That(pair.Second, Is.EqualTo(0x0403));
        }
    }

    // ReadArray applies the byte order to every element.
    [Test]
    public void ReadArray_ShortsBigEndian_ReadsEveryElement()
    {
        BufferMemory memory = new(new Address(Base), [0x00, 0x01, 0x00, 0x02, 0x01, 0x00], MemoryLayout.BigEndian32);

        Assert.That(memory.ReadArray<short>(new Address(Base), 3).Unwrap(), Is.EqualTo(new short[] { 1, 2, 256 }));
    }

    // ReadArray with a count of 0 returns an empty array without reading.
    [Test]
    public void ReadArray_CountZero_ReturnsEmpty()
    {
        BufferMemory memory = new(new Address(Base), [], MemoryLayout.X86);

        Assert.That(memory.ReadArray<int>(new Address(Base), 0).Unwrap(), Is.Empty);
    }

    // ReadArray past the end of readable memory returns an error.
    [Test]
    public void ReadArray_PastTheEnd_ReturnsAnError()
    {
        BufferMemory memory = new(new Address(Base), [1, 0, 0, 0], MemoryLayout.X86);

        Assert.That(memory.ReadArray<int>(new Address(Base), 2).IsErr, Is.True);
    }

    private readonly record struct Pair(
        short First,
        short Second);
}
