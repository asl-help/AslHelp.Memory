using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System;
using System.Linq;

namespace AslHelp.Memory.Tests.Scanning;

[TestFixture]
public class ScanTests
{
    private static readonly Address _base = new(0x1000);

    // A single match is returned at its address.
    [Test]
    public void ScanRange_OneMatch_ReturnsItsAddress()
    {
        BufferMemory memory = new(_base, [0x00, 0xAB, 0xCD, 0x00], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, ScanPattern.Parse("AB CD"));

        Assert.That(found, Is.EqualTo([_base + 1]));
    }

    // Every match is returned in ascending order, overlapping ones included.
    [Test]
    public void ScanRange_OverlappingMatches_ReturnsEachOne()
    {
        BufferMemory memory = new(_base, [0xAA, 0xAA, 0xAA, 0x00, 0xAA], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, ScanPattern.Parse("AA AA"));

        Assert.That(found, Is.EqualTo([_base, _base + 1]));
    }

    // Wildcards match any byte while fixed bytes still have to match.
    [Test]
    public void ScanRange_Wildcards_MatchAnyByte()
    {
        BufferMemory memory = new(_base, [0x48, 0x11, 0x05, 0x48, 0x22, 0x06, 0x48, 0x33, 0x05], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, ScanPattern.Parse("48 ?? 05"));

        Assert.That(found, Is.EqualTo([_base, _base + 6]));
    }

    // A pattern with no fully fixed byte is still checked at each offset.
    [Test]
    public void ScanRange_OnlyNibbles_MatchesByNibble()
    {
        BufferMemory memory = new(_base, [0x12, 0x4F, 0x40, 0x3F], MemoryLayout.X64);

        var found = Scan.ScanRange(memory, memory.Range, ScanPattern.Parse("4? ?F"));

        Assert.That(found, Is.EqualTo([_base + 2]));
    }

    // A chunk that can't be read is skipped while the readable chunks are still searched.
    [Test]
    public void ScanRange_AnUnreadableChunk_IsSkipped()
    {
        byte[] bytes = new byte[Scan.ChunkSize];
        bytes[10] = 0xAB;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        var found = Scan.ScanRange(memory, new(_base, Scan.ChunkSize * 3UL), ScanPattern.Parse("AB"));

        Assert.That(found, Is.EqualTo([_base + 10]));
    }

    // An unreadable range is skipped and the next range is still searched.
    [Test]
    public void ScanRanges_AnUnreadableRange_IsSkipped()
    {
        BufferMemory memory = new(_base, [0x00, 0xAB], MemoryLayout.X64);
        MemoryRange[] ranges = [new(new Address(0x9000), 0x100), memory.Range];

        var found = Scan.ScanRanges(memory, ranges, ScanPattern.Parse("AB"));

        Assert.That(found, Is.EqualTo([_base + 1]));
    }

    // A range shorter than the pattern matches nowhere.
    [Test]
    public void ScanRange_ShorterThanThePattern_ReturnsNothing()
    {
        BufferMemory memory = new(_base, [0xAB], MemoryLayout.X64);

        Assert.That(Scan.ScanRange(memory, memory.Range, ScanPattern.Parse("AB CD")), Is.Empty);
    }

    // ScanRegions searches only the regions that can be read.
    [Test]
    public void ScanRegions_MixedProtection_SearchesOnlyReadableRegions()
    {
        ProviderMemory memory = new(new(_base, [0xAB, 0x00, 0xAB, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.None));
        memory.Regions.Add(new(new(_base + 2, 2), MemoryProtection.Read | MemoryProtection.Execute));

        var found = Scan.ScanRegions(memory, ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 2]));
    }

    // A match split across 2 adjacent readable regions is found once.
    [Test]
    public void ScanRegions_AMatchAcrossAdjacentRegions_IsFoundOnce()
    {
        ProviderMemory memory = new(new(_base, [0x00, 0xAB, 0xCD, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 2, 2), MemoryProtection.Read | MemoryProtection.Write));

        var found = Scan.ScanRegions(memory, ScanPattern.Parse("AB CD")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 1]));
    }

    // A gap between 2 readable regions still separates them, so a match across the gap isn't found.
    [Test]
    public void ScanRegions_AMatchAcrossAGap_IsNotFound()
    {
        ProviderMemory memory = new(new(_base, [0x00, 0xAB, 0xCD, 0xEF, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 3, 2), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, ScanPattern.Parse("AB CD EF")).Unwrap();

        Assert.That(found, Is.Empty);
    }

    // A region that starts before the range is cut at the range start, so a match before it isn't returned.
    [Test]
    public void ScanRegions_ARegionStartingBeforeTheRange_SkipsMatchesBeforeIt()
    {
        ProviderMemory memory = new(new(_base, [0xAB, 0x00, 0xAB, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 4), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(_base + 2, 2), ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 2]));
    }

    // A match whose last byte is past the range end isn't returned, even though its region has it.
    [Test]
    public void ScanRegions_AMatchAcrossTheRangeEnd_IsNotFound()
    {
        ProviderMemory memory = new(new(_base, [0x00, 0xAB, 0xCD, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 4), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(_base, 2), ScanPattern.Parse("AB CD")).Unwrap();

        Assert.That(found, Is.Empty);
    }

    // Only the readable regions inside the range are searched, so an unreadable region and a region past the range are skipped.
    [Test]
    public void ScanRegions_InARange_SearchesOnlyReadableRegions()
    {
        ProviderMemory memory = new(new(_base, [0xAB, 0x00, 0xAB, 0x00, 0xAB, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.None));
        memory.Regions.Add(new(new(_base + 2, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 4, 2), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(_base, 4), ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 2]));
    }

    // A range over 2 adjacent readable regions finds a match across both once.
    [Test]
    public void ScanRegions_ARangeAcrossAdjacentRegions_FindsAMatchAcrossThem()
    {
        ProviderMemory memory = new(new(_base, [0x00, 0xAB, 0xCD, 0xAB, 0xCD], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 2, 3), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(_base, 3), ScanPattern.Parse("AB CD")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 1]));
    }

    // A range whose end wraps to 0 is still cut and searched.
    [Test]
    public void ScanRegions_ARangeEndingAtTheTopOfMemory_IsSearched()
    {
        Address top = new(ulong.MaxValue - 7);
        ProviderMemory memory = new(new(top, [0xAB, 0x00, 0x00, 0x00, 0x00, 0x00, 0xAB, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(top, 8), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(top + 4, 4), ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.EqualTo([top + 6]));
    }

    // The scan stops reading once the caller takes the first match.
    [Test]
    public void ScanRange_TakingTheFirstMatch_StopsEarly()
    {
        CountingMemory memory = new(new(_base, new byte[Scan.ChunkSize * 4], MemoryLayout.X64));

        var first = Scan.ScanRange(memory, new(_base, Scan.ChunkSize * 4UL), ScanPattern.Parse("00")).First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(_base));
            Assert.That(memory.Reads, Is.EqualTo(1));
        }
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
