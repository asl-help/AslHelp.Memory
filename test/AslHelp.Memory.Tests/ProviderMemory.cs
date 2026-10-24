using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests;

/// <summary>
///     Provides reads over a <see cref="BufferMemory"/> together with listed regions and modules.
/// </summary>
/// <param name="memory">The memory to read from.</param>
internal sealed class ProviderMemory(
    BufferMemory memory) : IMemoryReader, IMemoryRegionProvider, IMemoryModuleProvider
{
    public MemoryLayout Layout => memory.Layout;

    public List<MemoryRegion> Regions { get; } = [];

    public List<MemoryModule> Modules { get; } = [];

    public Result Read(Address address, Span<byte> buffer)
    {
        return memory.Read(address, buffer);
    }

    public Result<IReadOnlyList<MemoryRegion>> GetRegions(MemoryRange range)
    {
        return Regions.FindAll(region => region.Range.Start < range.End && range.Start < region.Range.End);
    }

    public Result<IReadOnlyList<MemoryModule>> GetModules()
    {
        return Modules;
    }
}
