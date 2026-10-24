using AslHelp.Memory.Scanning;
using NUnit.Framework;

namespace AslHelp.Memory.Tests.Scanning;

[TestFixture]
public class AddressResolverTests
{
    private static readonly Address _base = new(0x1000);

    // On X64 Relative adds the displacement to the end of the instruction.
    [Test]
    public void Relative_X64_AddsTheDisplacementToTheInstructionEnd()
    {
        BufferMemory memory = new(_base, [0x48, 0x8B, 0x05, 0x10, 0x00, 0x00, 0x00], MemoryLayout.X64);

        var resolved = AddressResolver.Relative(3)(memory, _base);

        Assert.That(resolved.Unwrap(), Is.EqualTo(_base + 7 + 0x10));
    }

    // On X64 a negative displacement moves back from the end of the instruction.
    [Test]
    public void Relative_X64NegativeDisplacement_MovesBack()
    {
        BufferMemory memory = new(_base, [0x48, 0x8B, 0x05, 0xF0, 0xFF, 0xFF, 0xFF], MemoryLayout.X64);

        var resolved = AddressResolver.Relative(3)(memory, _base);

        Assert.That(resolved.Unwrap(), Is.EqualTo(_base + 7 - 0x10));
    }

    // On X86 Relative reads the operand as an absolute address.
    [Test]
    public void Relative_X86_ReadsTheAbsoluteAddress()
    {
        BufferMemory memory = new(_base, [0xA1, 0x78, 0x56, 0x34, 0x12], MemoryLayout.X86);

        var resolved = AddressResolver.Relative(1)(memory, _base);

        Assert.That(resolved.Unwrap(), Is.EqualTo(new Address(0x12345678)));
    }

    // On a big-endian 32-bit layout Relative reads the operand with its highest byte first.
    [Test]
    public void Relative_BigEndian32_ReadsHighByteFirst()
    {
        BufferMemory memory = new(_base, [0x12, 0x34, 0x56, 0x78], MemoryLayout.BigEndian32);

        var resolved = AddressResolver.Relative(0)(memory, _base);

        Assert.That(resolved.Unwrap(), Is.EqualTo(new Address(0x12345678)));
    }

    // A pointer size other than 4 or 8 fails.
    [Test]
    public void Relative_ThreeBytePointers_ReturnsAnError()
    {
        BufferMemory memory = new(_base, new byte[8], new MemoryLayout(3, ByteOrder.LittleEndian));

        Assert.That(AddressResolver.Relative(0)(memory, _base).IsErr, Is.True);
    }

    // A displacement that can't be read fails.
    [Test]
    public void Relative_PastTheEnd_ReturnsAnError()
    {
        BufferMemory memory = new(_base, [0x48, 0x8B, 0x05], MemoryLayout.X64);

        Assert.That(AddressResolver.Relative(3)(memory, _base).IsErr, Is.True);
    }

    // On X86 Displacement adds the distance to the end of the instruction.
    [Test]
    public void Displacement_X86_AddsTheDistanceToTheInstructionEnd()
    {
        BufferMemory memory = new(_base, [0xE8, 0x20, 0x00, 0x00, 0x00], MemoryLayout.X86);

        var resolved = AddressResolver.Displacement(1)(memory, _base);

        Assert.That(resolved.Unwrap(), Is.EqualTo(_base + 5 + 0x20));
    }

    // Displacement counts from the end of the given instruction size.
    [Test]
    public void Displacement_ALongerTail_CountsFromItsEnd()
    {
        BufferMemory memory = new(_base, [0x0F, 0x85, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00], MemoryLayout.X64);

        var resolved = AddressResolver.Displacement(2, instructionSize: 6)(memory, _base);

        Assert.That(resolved.Unwrap(), Is.EqualTo(_base + 8 + 8));
    }

    // Offset moves the match without reading.
    [Test]
    public void Offset_ANegativeDistance_MovesBackWithoutReading()
    {
        BufferMemory memory = new(_base, [], MemoryLayout.X64);

        Assert.That(AddressResolver.Offset(-0x10)(memory, _base).Unwrap(), Is.EqualTo(_base - 0x10));
    }
}
