using AslHelp.Memory.Errors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AslHelp.Memory.Tests;

/// <summary>
///     Provides reads and writes over a byte array placed at a base address, with listed regions and modules.
/// </summary>
/// <param name="baseAddress">The address of the first byte.</param>
/// <param name="bytes">The bytes the memory consists of.</param>
/// <param name="layout">The layout of the memory.</param>
internal sealed class BufferMemory(
    ulong baseAddress,
    byte[] bytes,
    MemoryLayout layout) : IMemoryReader, IMemoryWriter, IMemoryRegionProvider, IMemoryModuleProvider
{
    public MemoryLayout Layout { get; } = layout;

    public List<MemoryRegion> Regions { get; } = [];

    public List<MemoryModule> Modules { get; } = [];

    public Result Read(Address address, Span<byte> buffer)
    {
        if (!TryFindOffset(address, buffer.Length, out int offset))
        {
            return MemoryError.Unreadable(address, buffer.Length);
        }

        bytes.AsSpan(offset, buffer.Length).CopyTo(buffer);
        return Result.Ok();
    }

    public Result Write(Address address, ReadOnlySpan<byte> buffer)
    {
        if (!TryFindOffset(address, buffer.Length, out int offset))
        {
            return MemoryError.Unwritable(address, buffer.Length);
        }

        buffer.CopyTo(bytes.AsSpan(offset));
        return Result.Ok();
    }

    public Result<IReadOnlyList<MemoryRegion>> GetRegions(MemoryRange range)
    {
        return Regions.Where(region => region.Range.Start < range.End && range.Start < region.Range.End).ToList();
    }

    public Result<IReadOnlyList<MemoryModule>> GetModules()
    {
        return Modules;
    }

    private bool TryFindOffset(Address address, int length, out int offset)
    {
        offset = 0;
        if (address.Value < baseAddress || address.Value - baseAddress + (ulong)length > (ulong)bytes.Length)
        {
            return false;
        }

        offset = (int)(address.Value - baseAddress);
        return true;
    }
}
