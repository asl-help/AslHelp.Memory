using NUnit.Framework;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class MemoryRangeTests
{
    // End is the address one past the last byte of the range.
    [Test]
    public void End_ARange_IsStartPlusSize()
    {
        MemoryRange range = new(new Address(0x1000), 0x100);

        Assert.That(range.End, Is.EqualTo(new Address(0x1100)));
    }

    // Contains accepts the start and the last byte but refuses the end.
    [Test]
    public void Contains_TheBounds_IncludesStartExcludesEnd()
    {
        MemoryRange range = new(new Address(0x1000), 0x100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(range.Contains(new Address(0x1000)), Is.True);
            Assert.That(range.Contains(new Address(0x10FF)), Is.True);
            Assert.That(range.Contains(new Address(0x1100)), Is.False);
            Assert.That(range.Contains(new Address(0xFFF)), Is.False);
        }
    }
}
