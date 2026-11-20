using AslHelp.Memory.Scanning;
using NUnit.Framework;

namespace AslHelp.Memory.Tests.Scanning;

[TestFixture]
public class ScanRegionsEdgeTests
{
    private static readonly Address _base = new(0x1000);

    // A range of 0 bytes doesn't find anything, even inside a readable region with a match at its start.
    [Test]
    public void ScanRegions_EmptyRange_FindsNothing()
    {
        var memory = NewMemory([0xAB, 0xAB, 0xAB, 0xAB]);
        memory.Regions.Add(new(new(_base, 4), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(_base + 1, 0), ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.Empty);
    }

    // A range between 2 regions that touches neither doesn't find anything.
    [Test]
    public void ScanRegions_RangeBetweenRegions_FindsNothing()
    {
        var memory = NewMemory([0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB]);
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 4, 2), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(_base + 2, 2), ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.Empty);
    }

    // A range inside 1 region is cut at both ends and still finds a match that ends on its last byte.
    [Test]
    public void ScanRegions_RangeInsideARegion_FindsMatchesUpToTheLastByte()
    {
        var memory = NewMemory([0xAB, 0xCD, 0xAB, 0xCD, 0x00, 0xAB, 0xCD, 0xAB, 0xCD]);
        memory.Regions.Add(new(new(_base, 9), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(_base + 2, 5), ScanPattern.Parse("AB CD")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 2, _base + 5]));
    }

    // A range that starts before a region and ends inside it searches only the part of the region in the range.
    [Test]
    public void ScanRegions_RangeStartingBeforeARegion_SearchesTheOverlap()
    {
        var memory = NewMemory([0xAB, 0xAB, 0xAB, 0xAB, 0xAB, 0xAB]);
        memory.Regions.Add(new(new(_base + 2, 4), MemoryProtection.Read));

        var found = Scan.ScanRegions(memory, new(_base, 4), ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.EqualTo([_base + 2, _base + 3]));
    }

    // Matches from several regions come back in ascending order.
    [Test]
    public void ScanRegions_MatchesInSeveralRegions_ReturnsThemInOrder()
    {
        var memory = NewMemory([0xAB, 0x00, 0xAB, 0x00, 0xAB, 0x00, 0xAB, 0x00]);
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 2, 2), MemoryProtection.None));
        memory.Regions.Add(new(new(_base + 4, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 6, 2), MemoryProtection.Read | MemoryProtection.Write));

        var found = Scan.ScanRegions(memory, new(_base, 8), ScanPattern.Parse("AB")).Unwrap();

        Assert.That(found, Is.EqualTo([_base, _base + 4, _base + 6]));
    }

    // A range over all memory finds the same matches as the overload without a range.
    [Test]
    public void ScanRegions_RangeOverAllMemory_MatchesTheOverloadWithoutARange()
    {
        var memory = NewMemory([0xAB, 0x00, 0xAB, 0x00, 0xAB, 0x00]);
        memory.Regions.Add(new(new(_base, 2), MemoryProtection.Read));
        memory.Regions.Add(new(new(_base + 2, 2), MemoryProtection.None));
        memory.Regions.Add(new(new(_base + 4, 2), MemoryProtection.Read));

        var all = Scan.ScanRegions(memory, ScanPattern.Parse("AB")).Unwrap();
        var ranged = Scan.ScanRegions(memory, new(Address.Zero, ulong.MaxValue), ScanPattern.Parse("AB")).Unwrap();

        Assert.That(ranged, Is.EqualTo(all));
    }

    private static ProviderMemory NewMemory(byte[] bytes)
    {
        return new(new(_base, bytes, MemoryLayout.X64));
    }
}
