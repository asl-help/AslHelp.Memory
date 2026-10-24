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
    public void Range_OneMatch_ReturnsItsAddress()
    {
        BufferMemory memory = new(_base, [0x00, 0xAB, 0xCD, 0x00], MemoryLayout.X64);

        var found = Scan.Range(memory, memory.Range, ScanPattern.Parse("AB CD"));

        Assert.That(found, Is.EqualTo([_base + 1]));
    }

    // Every match is returned in ascending order, overlapping ones included.
    [Test]
    public void Range_OverlappingMatches_ReturnsEachOne()
    {
        BufferMemory memory = new(_base, [0xAA, 0xAA, 0xAA, 0x00, 0xAA], MemoryLayout.X64);

        var found = Scan.Range(memory, memory.Range, ScanPattern.Parse("AA AA"));

        Assert.That(found, Is.EqualTo([_base, _base + 1]));
    }

    // Wildcards match any byte while fixed bytes still have to match.
    [Test]
    public void Range_Wildcards_MatchAnyByte()
    {
        BufferMemory memory = new(_base, [0x48, 0x11, 0x05, 0x48, 0x22, 0x06, 0x48, 0x33, 0x05], MemoryLayout.X64);

        var found = Scan.Range(memory, memory.Range, ScanPattern.Parse("48 ?? 05"));

        Assert.That(found, Is.EqualTo([_base, _base + 6]));
    }

    // A pattern with no fully fixed byte is still checked at each offset.
    [Test]
    public void Range_OnlyNibbles_MatchesByNibble()
    {
        BufferMemory memory = new(_base, [0x12, 0x4F, 0x40, 0x3F], MemoryLayout.X64);

        var found = Scan.Range(memory, memory.Range, ScanPattern.Parse("4? ?F"));

        Assert.That(found, Is.EqualTo([_base + 2]));
    }

    // A match that starts in 1 chunk and ends in the next is found once.
    [Test]
    public void Range_AMatchAcrossTheChunkBoundary_IsFoundOnce()
    {
        byte[] bytes = new byte[Scan.ChunkSize + 16];
        bytes.AsSpan(Scan.ChunkSize - 2, 4).Fill(0xAB);
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        var found = Scan.Range(memory, memory.Range, ScanPattern.Parse("AB AB AB AB"));

        Assert.That(found, Is.EqualTo([_base + (Scan.ChunkSize - 2)]));
    }

    // A chunk that can't be read is skipped while the readable chunks are still searched.
    [Test]
    public void Range_AnUnreadableChunk_IsSkipped()
    {
        byte[] bytes = new byte[Scan.ChunkSize];
        bytes[10] = 0xAB;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        var found = Scan.Range(memory, new(_base, Scan.ChunkSize * 3UL), ScanPattern.Parse("AB"));

        Assert.That(found, Is.EqualTo([_base + 10]));
    }

    // An unreadable range is skipped and the next range is still searched.
    [Test]
    public void Ranges_AnUnreadableRange_IsSkipped()
    {
        BufferMemory memory = new(_base, [0x00, 0xAB], MemoryLayout.X64);
        MemoryRange[] ranges = [new(new Address(0x9000), 0x100), memory.Range];

        var found = Scan.Ranges(memory, ranges, ScanPattern.Parse("AB"));

        Assert.That(found, Is.EqualTo([_base + 1]));
    }

    // A range shorter than the pattern matches nowhere.
    [Test]
    public void Range_ShorterThanThePattern_ReturnsNothing()
    {
        BufferMemory memory = new(_base, [0xAB], MemoryLayout.X64);

        Assert.That(Scan.Range(memory, memory.Range, ScanPattern.Parse("AB CD")), Is.Empty);
    }

    // Regions searches only the regions that can be read.
    [Test]
    public void Regions_MixedProtection_SearchesOnlyReadableRegions()
    {
        ProviderMemory memory = new(new(_base, [0xAB, 0x00, 0xAB, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.None));
        memory.Regions.Add(new(new(_base + 2, 2), MemoryProtection.Read | MemoryProtection.Execute));

        var found = Scan.Regions(memory, ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 2]));
    }

    // A match split across 2 adjacent readable regions is found once.
    [Test]
    public void Regions_AMatchAcrossAdjacentRegions_IsFoundOnce()
    {
        ProviderMemory memory = new(new(_base, [0x00, 0xAB, 0xCD, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 2, 2), MemoryProtection.Read | MemoryProtection.Write));

        var found = Scan.Regions(memory, ScanPattern.Parse("AB CD")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 1]));
    }

    // A gap between 2 readable regions still separates them, so a match across the gap isn't found.
    [Test]
    public void Regions_AMatchAcrossAGap_IsNotFound()
    {
        ProviderMemory memory = new(new(_base, [0x00, 0xAB, 0xCD, 0xEF, 0x00], MemoryLayout.X64));
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 3, 2), MemoryProtection.Read));

        var found = Scan.Regions(memory, ScanPattern.Parse("AB CD EF")).Unwrap();

        Assert.That(found, Is.Empty);
    }

    // The scan stops reading once the caller takes the first match.
    [Test]
    public void Range_TakingTheFirstMatch_StopsEarly()
    {
        CountingMemory memory = new(new(_base, new byte[Scan.ChunkSize * 4], MemoryLayout.X64));

        var first = Scan.Range(memory, new(_base, Scan.ChunkSize * 4UL), ScanPattern.Parse("00")).First();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(_base));
            Assert.That(memory.Reads, Is.EqualTo(1));
        }
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
