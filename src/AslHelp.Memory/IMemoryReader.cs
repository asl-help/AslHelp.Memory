using System;

namespace AslHelp.Memory;

/// <summary>
///     Provides reads of raw bytes from a target's memory.
/// </summary>
public interface IMemoryReader
{
    /// <summary>
    ///     Gets the pointer size and byte order of the memory.
    /// </summary>
    MemoryLayout Layout { get; }

    /// <summary>
    ///     Reads <paramref name="buffer"/>'s length in bytes starting at <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the first byte.</param>
    /// <param name="buffer">The buffer to fill.</param>
    /// <returns>
    ///     A success when every byte was read, otherwise a failed result.
    /// </returns>
    Result Read(Address address, Span<byte> buffer);
}
