using NUnit.Framework;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class MemoryRegionExtensionsTests
{
    private static readonly MemoryRegion _first = new(new(new Address(0x1000), 0x1000), MemoryProtection.Read);
    private static readonly MemoryRegion _second = new(new(new Address(0x2000), 0x1000), MemoryProtection.Read | MemoryProtection.Write);
    private static readonly MemoryRegion _third = new(new(new Address(0x3000), 0x1000), MemoryProtection.Read);

    // Adjacent readable regions join into 1 range.
    [Test]
    public void MergeReadable_AdjacentRegions_JoinsThem()
    {
        MemoryRegion[] regions = [_first, _second, _third];

        Assert.That(regions.MergeReadable(), Is.EqualTo([new MemoryRange(new Address(0x1000), 0x3000)]));
    }

    // A gap between 2 readable regions keeps them apart.
    [Test]
    public void MergeReadable_AGap_KeepsTheRegionsApart()
    {
        MemoryRegion[] regions = [_first, _third];

        Assert.That(regions.MergeReadable(), Is.EqualTo([_first.Range, _third.Range]));
    }

    // A region that can't be read ends a range and is dropped.
    [Test]
    public void MergeReadable_AnUnreadableRegion_EndsTheRangeAndIsDropped()
    {
        MemoryRegion[] regions = [_first, _second with { Protection = MemoryProtection.Execute }, _third];

        Assert.That(regions.MergeReadable(), Is.EqualTo([_first.Range, _third.Range]));
    }

    // No regions give no ranges.
    [Test]
    public void MergeReadable_NoRegions_ReturnsNothing()
    {
        IEnumerable<MemoryRegion> regions = [];

        Assert.That(regions.MergeReadable(), Is.Empty);
    }
}
