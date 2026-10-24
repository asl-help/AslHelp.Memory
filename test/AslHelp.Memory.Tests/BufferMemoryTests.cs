using NUnit.Framework;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class BufferMemoryTests
{
    private static readonly Address _base = new(0x1000);

    // Range spans the array from the base address.
    [Test]
    public void Range_AnArray_StartsAtTheBaseAndSpansTheArray()
    {
        BufferMemory memory = new(_base, new byte[16], MemoryLayout.X64);

        Assert.That(memory.Range, Is.EqualTo(new MemoryRange(_base, 16)));
    }

    // A write changes the mapped array.
    [Test]
    public void Write_InsideTheArray_ChangesTheArray()
    {
        byte[] bytes = new byte[4];
        BufferMemory memory = new(_base, bytes, MemoryLayout.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(memory.Write(_base + 1, [0xAB, 0xCD]).IsOk, Is.True);
            Assert.That(bytes, Is.EqualTo(new byte[] { 0x00, 0xAB, 0xCD, 0x00 }));
        }
    }

    // A read that starts inside the array but ends past it fails whole.
    [Test]
    public void Read_PastTheEnd_ReturnsAnError()
    {
        BufferMemory memory = new(_base, new byte[4], MemoryLayout.X86);

        Assert.That(memory.Read(_base + 2, new byte[4]).IsErr, Is.True);
    }

    // A read below the base address fails.
    [Test]
    public void Read_BelowTheBase_ReturnsAnError()
    {
        BufferMemory memory = new(_base, new byte[4], MemoryLayout.X86);

        Assert.That(memory.Read(_base - 1, new byte[1]).IsErr, Is.True);
    }

    // A write longer than the array fails instead of wrapping the bounds check.
    [Test]
    public void Write_LongerThanTheArray_ReturnsAnError()
    {
        BufferMemory memory = new(_base, new byte[2], MemoryLayout.X86);

        Assert.That(memory.Write(_base, new byte[3]).IsErr, Is.True);
    }
}
