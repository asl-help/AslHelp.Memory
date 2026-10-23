using NUnit.Framework;
using System;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class MemoryLayoutTests
{
    // A pointer size of 0 is refused.
    [Test]
    public void Constructor_PointerSizeZero_Throws()
    {
        Assert.That(() => new MemoryLayout(0, ByteOrder.LittleEndian), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // A pointer size above 8 is refused.
    [Test]
    public void Constructor_PointerSizeNine_Throws()
    {
        Assert.That(() => new MemoryLayout(9, ByteOrder.LittleEndian), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // An odd pointer size between 1 and 8 is accepted.
    [Test]
    public void Constructor_PointerSizeThree_KeepsIt()
    {
        MemoryLayout layout = new(3, ByteOrder.BigEndian);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.PointerSize, Is.EqualTo(3));
            Assert.That(layout.ByteOrder, Is.EqualTo(ByteOrder.BigEndian));
        }
    }

    // The presets have the pointer size and byte order of their platform.
    [Test]
    public void Presets_Always_MatchTheirPlatform()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(MemoryLayout.X86, Is.EqualTo(new MemoryLayout(4, ByteOrder.LittleEndian)));
            Assert.That(MemoryLayout.X64, Is.EqualTo(new MemoryLayout(8, ByteOrder.LittleEndian)));
            Assert.That(MemoryLayout.BigEndian32, Is.EqualTo(new MemoryLayout(4, ByteOrder.BigEndian)));
        }
    }
}
