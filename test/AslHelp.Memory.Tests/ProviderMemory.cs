using AslHelp.Memory.Errors;
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
        // Compares starts with Contains, because End wraps to 0 for a region at the top of memory.
        return Regions.FindAll(region => range.Contains(region.Range.Start) || region.Range.Contains(range.Start));
    }

    public Result<IReadOnlyList<MemoryModule>> GetModules()
    {
        return Modules;
    }

    public Result<MemoryModule> GetMainModule()
    {
        // Returns the 1st listed module, as Windows lists the executable 1st.
        return Modules is [var main, ..] ? main : MemoryError.ModuleNotFound("main");
    }
}
