using NUnit.Framework;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class MemoryRegionProviderTests
{
    // GetRegions keeps the regions that overlap the range and drops the ones that only touch its ends.
    [Test]
    public void GetRegions_ARange_ReturnsOnlyOverlappingRegions()
    {
        ProviderMemory memory = new(new(Address.Zero, [], MemoryLayout.X64));
        MemoryRegion before = new(new(new Address(0x1000), 0x1000), MemoryProtection.Read);
        MemoryRegion start = new(new(new Address(0x2000), 0x1000), MemoryProtection.Read | MemoryProtection.Write);
        MemoryRegion end = new(new(new Address(0x3000), 0x1000), MemoryProtection.Execute);
        MemoryRegion after = new(new(new Address(0x4000), 0x1000), MemoryProtection.None);
        memory.Regions.AddRange([before, start, end, after]);

        var regions = memory.GetRegions(new(new Address(0x2800), 0x1800)).Unwrap();

        Assert.That(regions, Is.EqualTo([start, end]));
    }
}
