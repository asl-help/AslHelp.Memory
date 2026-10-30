using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System;
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

    private sealed class CountingMemory(
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
}
