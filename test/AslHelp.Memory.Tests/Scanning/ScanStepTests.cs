using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Memory.Tests.Scanning;

[TestFixture]
public class ScanStepTests
{
    private static readonly Address _base = new(0x1000);

    // A Forward step searches ahead of the previous match.
    [Test]
    public void ScanRange_AForwardStep_SearchesAheadOfTheMatch()
    {
        BufferMemory memory = new(_base, [0x99, 0xAB, 0xCD, 0x00, 0x99], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, Scan.Create("AB CD"), ScanWindow.Forward(0x10).Create("99"));

        Assert.That(found, Is.EqualTo([_base + 4]));
    }

    // A Forward window that stops short of the pattern finds nothing.
    [Test]
    public void ScanRange_AShortForwardWindow_FindsNothing()
    {
        BufferMemory memory = new(_base, [0xAB, 0xCD, 0x00, 0x99], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, Scan.Create("AB CD"), ScanWindow.Forward(3).Create("99"));

        Assert.That(found, Is.Empty);
    }

    // A negative window is empty, so a size computed below 0 finds nothing instead of throwing.
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void ScanRange_ANegativeWindow_FindsNothing(int size)
    {
        BufferMemory memory = new(_base, [0x99, 0xAB, 0xCD, 0x99], MemoryLayout.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Scan.ScanRange(memory, memory.Range, Scan.Create("AB CD"), ScanWindow.Forward(size).Create("99")), Is.Empty);
            Assert.That(Scan.ScanRange(memory, memory.Range, Scan.Create("AB CD"), ScanWindow.Backward(size).Create("99")), Is.Empty);
        }
    }

    // A window that reaches past the end of readable memory still searches the readable part.
    [Test]
    public void ScanRange_AWindowPastTheEndOfReadableMemory_SearchesTheReadablePart()
    {
        byte[] bytes = new byte[0x3000];
        bytes[0x2FF0] = 0x99;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        var found = Scan.ScanRange(memory, new(_base, 0x10000), ScanWindow.Forward(0x8000).Create("99"));

        Assert.That(found, Is.EqualTo([_base + 0x2FF0]));
    }

    // A window across an unreadable page still searches the pages on both sides of it.
    [Test]
    public void ScanRange_AWindowAcrossAnUnreadablePage_SearchesBothSides()
    {
        byte[] bytes = new byte[0x4000];
        bytes[0x800] = 0x99;
        bytes[0x3800] = 0x99;
        HoleMemory memory = new(new(_base, bytes, MemoryLayout.X64), new(_base + 0x2000, 0x1000));

        var found = Scan.ScanRange(memory, new(_base, 0x4000), ScanWindow.Forward(0x4000).Create("99"));

        Assert.That(found, Is.EqualTo([_base + 0x800, _base + 0x3800]));
    }

    // A window that ends half a page into an unreadable page still searches the readable page before it.
    [Test]
    public void ScanRange_AWindowEndingInAnUnreadablePage_SearchesThePageBefore()
    {
        byte[] bytes = new byte[0x2000];
        bytes[0xE00] = 0x99;
        HoleMemory memory = new(new(_base, bytes, MemoryLayout.X64), new(_base + 0x1000, 0x1000));

        var found = Scan.ScanRange(memory, new(_base, 0x1800), ScanWindow.Forward(0x1800).Create("99"));

        Assert.That(found, Is.EqualTo([_base + 0xE00]));
    }

    // A window that starts inside a page splits at the page borders, so the part before the unreadable page is searched.
    [Test]
    public void ScanRange_AWindowStartingInsideAPage_SplitsAtThePageBorders()
    {
        byte[] bytes = new byte[0x3000];
        bytes[0xF00] = 0x99;
        HoleMemory memory = new(new(_base, bytes, MemoryLayout.X64), new(_base + 0x1000, 0x1000));

        var found = Scan.ScanRange(memory, new(_base + 0x100, 0x1800), ScanWindow.Forward(0x1800).Create("99"));

        Assert.That(found, Is.EqualTo([_base + 0xF00]));
    }

    // A reader that lists its regions reads each readable part once instead of splitting the failed read.
    [Test]
    public void ScanRange_AWindowOverListedRegions_ReadsEachReadablePartOnce()
    {
        byte[] bytes = new byte[0x4000];
        bytes[0x800] = 0x99;
        bytes[0x3800] = 0x99;
        RegionHoleMemory memory = new(new(_base, bytes, MemoryLayout.X64), new(_base + 0x2000, 0x1000));

        var found = Scan.ScanRange(memory, new(_base, 0x4000), ScanWindow.Forward(0x4000).Create("99")).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.EqualTo([_base + 0x800, _base + 0x3800]));
            Assert.That(memory.Reads, Is.EqualTo(3));
        }
    }

    // A Backward step searches behind the previous match.
    [Test]
    public void ScanRange_ABackwardStep_SearchesBehindTheMatch()
    {
        BufferMemory memory = new(_base, [0x99, 0x00, 0xAB, 0xCD, 0x99], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, Scan.Create("AB CD"), ScanWindow.Backward(0x10).Create("99"));

        Assert.That(found, Is.EqualTo([_base]));
    }

    // A Backward window that starts after the pattern finds nothing.
    [Test]
    public void ScanRange_AShortBackwardWindow_FindsNothing()
    {
        BufferMemory memory = new(_base, [0x99, 0x00, 0xAB, 0xCD], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, Scan.Create("AB CD"), ScanWindow.Backward(1).Create("99"));

        Assert.That(found, Is.Empty);
    }

    // The next step searches around every match of a step.
    [Test]
    public void ScanRange_AStepMatchingTwice_SearchesAroundBothMatches()
    {
        BufferMemory memory = new(_base, [0xAB, 0xCD, 0x11, 0xAB, 0xCD, 0x11], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, Scan.Create("AB CD"), ScanWindow.Forward(3).Create("11"));

        Assert.That(found, Is.EqualTo([_base + 2, _base + 5]));
    }

    // A chain finds a call, follows its displacement and resolves the RIP-relative load at its target.
    [Test]
    public void ScanRange_AChainOfSteps_ResolvesTheGlobal()
    {
        byte[] bytes = new byte[0x40];

        // At 0x00, call +0x1B ends at 0x05 and goes to 0x20.
        bytes[0x00] = 0xE8;
        bytes[0x01] = 0x1B;

        // At 0x20, mov rax, [rip + 0x100] ends at 0x27.
        bytes[0x20] = 0x48;
        bytes[0x21] = 0x8B;
        bytes[0x22] = 0x05;
        bytes[0x24] = 0x01;

        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        var found = Scan.ScanRange(
            memory,
            memory.Range,
            Scan.Create("E8 ?? ?? ?? ??").WithResolver(AddressResolver.FromDisplacement(1)),
            ScanWindow.Forward(0x10).Create("48 8B 05").WithResolver(AddressResolver.FromRelative(3)));

        Assert.That(found, Is.EqualTo([_base + 0x127]));
    }

    // A resolver that leaves the range drops that match instead of ending the scan.
    [Test]
    public void ScanRange_AResolverLeavingTheRange_DropsThatMatch()
    {
        BufferMemory memory = new(_base, [0xAB, 0xAB, 0x99], MemoryLayout.X64);

        var found = Scan.ScanRange(
            memory,
            memory.Range,
            Scan.Create("AB").WithResolver(AddressResolver.FromOffset(2)),
            ScanWindow.Forward(1).Create("99"));

        Assert.That(found, Is.EqualTo([_base + 2]));
    }

    // A window larger than its stack space still returns every match inside it in order.
    [Test]
    public void ScanRange_ALargeForwardWindow_ReturnsEachMatch()
    {
        byte[] bytes = new byte[0x400];
        bytes[0x000] = 0xAB;
        bytes[0x010] = 0x99;
        bytes[0x300] = 0x99;
        bytes[0x3FF] = 0x99;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, Scan.Create("AB"), ScanWindow.Forward(0x3FF).Create("99"));

        Assert.That(found, Is.EqualTo([_base + 0x10, _base + 0x300]));
    }

    // A window larger than the range is clamped to the range end, so the last byte is still found.
    [Test]
    public void ScanRange_AWindowLargerThanTheRange_ClampsToTheRangeEnd()
    {
        BufferMemory memory = new(_base, [0xAB, 0x00, 0x00, 0x99], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, Scan.Create("AB"), ScanWindow.Forward(int.MaxValue).Create("99"));

        Assert.That(found, Is.EqualTo([_base + 3]));
    }

    // A window with 2 matches is read once for both of them.
    [Test]
    public void ScanRange_AWindowWithTwoMatches_ReadsTheWindowOnce()
    {
        BufferMemory inner = new(_base, [0xAB, 0x99, 0x00, 0x99], MemoryLayout.X64);
        CountingMemory memory = new(inner);
        var window = ScanWindow.Forward(4).Create("99");

        var found = Scan.ScanRange(memory, inner.Range, window).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.EqualTo([_base + 1, _base + 3]));
            Assert.That(memory.Reads, Is.EqualTo(1));
        }
    }

    // A chain with no steps throws.
    [Test]
    public void ScanRange_NoSteps_ThrowsArgumentException()
    {
        BufferMemory memory = new(_base, [], MemoryLayout.X64);

        Assert.Throws<ArgumentException>(() => Scan.ScanRange(memory, memory.Range, []));
    }
}

// Fails every read that touches the hole, like a guard page in the middle of mapped memory.
file sealed class HoleMemory(
    BufferMemory memory,
    MemoryRange hole) : IMemoryReader
{
    public MemoryLayout Layout => memory.Layout;

    public Result Read(Address address, Span<byte> buffer)
    {
        bool touchesHole = address < hole.End && address + buffer.Length > hole.Start;
        return memory.Read(touchesHole ? memory.Range.End : address, buffer);
    }
}

// Fails every read that touches the hole and lists the hole as a region that allows no access.
file sealed class RegionHoleMemory(
    BufferMemory memory,
    MemoryRange hole) : IMemoryReader, IMemoryRegionProvider
{
    public int Reads { get; private set; }

    public MemoryLayout Layout => memory.Layout;

    public Result Read(Address address, Span<byte> buffer)
    {
        Reads++;
        bool touchesHole = address < hole.End && address + buffer.Length > hole.Start;
        return memory.Read(touchesHole ? memory.Range.End : address, buffer);
    }

    public Result<IReadOnlyList<MemoryRegion>> GetRegions(MemoryRange range)
    {
        MemoryRegion[] regions =
        [
            new(new(memory.Range.Start, hole.Start.Value - memory.Range.Start.Value), MemoryProtection.Read),
            new(hole, MemoryProtection.None),
            new(new(hole.End, memory.Range.End.Value - hole.End.Value), MemoryProtection.Read),
        ];

        return regions;
    }
}

file sealed class CountingMemory(
    BufferMemory memory) : IMemoryReader
{
    public int Reads { get; private set; }

    public MemoryLayout Layout => memory.Layout;

    public Result Read(Address address, Span<byte> buffer)
    {
        Reads++;
        return memory.Read(address, buffer);
    }
}
