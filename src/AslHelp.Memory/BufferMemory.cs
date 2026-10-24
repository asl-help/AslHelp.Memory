using AslHelp.Memory.Errors;
using System;

namespace AslHelp.Memory;

/// <summary>
///     Provides reads and writes over a byte array mapped at a base address.
/// </summary>
/// <remarks>
///     The array isn't copied, so a write through this memory changes the caller's array. A read or write that
///     reaches outside the array fails.
/// </remarks>
/// <param name="baseAddress">The address of the first byte.</param>
/// <param name="bytes">The bytes the memory consists of.</param>
/// <param name="layout">The pointer size and byte order of the memory.</param>
public sealed class BufferMemory(
    Address baseAddress,
    byte[] bytes,
    MemoryLayout layout) : IMemoryReader, IMemoryWriter
{
    private readonly byte[] _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));

    /// <summary>
    ///     Gets the bytes the memory is mapped at.
    /// </summary>
    public MemoryRange Range { get; } = new(baseAddress, (ulong)bytes.Length);

    /// <inheritdoc/>
    public MemoryLayout Layout { get; } = layout;

    /// <inheritdoc/>
    public Result Read(Address address, Span<byte> buffer)
    {
        if (!TryFindOffset(address, buffer.Length, out int offset))
        {
            return MemoryError.Unreadable(address, buffer.Length);
        }

        _bytes.AsSpan(offset, buffer.Length).CopyTo(buffer);
        return Result.Ok();
    }

    /// <inheritdoc/>
    public Result Write(Address address, ReadOnlySpan<byte> buffer)
    {
        if (!TryFindOffset(address, buffer.Length, out int offset))
        {
            return MemoryError.Unwritable(address, buffer.Length);
        }

        buffer.CopyTo(_bytes.AsSpan(offset));
        return Result.Ok();
    }

    private bool TryFindOffset(Address address, int length, out int offset)
    {
        offset = 0;

        ulong start = Range.Start.Value;
        ulong size = (ulong)_bytes.Length;
        if (address.Value < start || (ulong)length > size || address.Value - start > size - (ulong)length)
        {
            return false;
        }

        offset = (int)(address.Value - start);
        return true;
    }
}
