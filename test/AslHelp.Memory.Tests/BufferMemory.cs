using System;

namespace AslHelp.Memory.Tests;

/// <summary>
///     Provides reads and writes over a byte array placed at a base address.
/// </summary>
/// <param name="baseAddress">The address of the first byte.</param>
/// <param name="bytes">The bytes the memory consists of.</param>
/// <param name="layout">The layout of the memory.</param>
internal sealed class BufferMemory(ulong baseAddress, byte[] bytes, MemoryLayout layout) : IMemoryReader, IMemoryWriter
{
    public MemoryLayout Layout { get; } = layout;

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
