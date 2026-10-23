using System;

namespace AslHelp.Memory;

/// <summary>
///     Provides writes of raw bytes to a target's memory.
/// </summary>
public interface IMemoryWriter
{
    /// <summary>
    ///     Writes <paramref name="buffer"/> starting at <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address of the first byte.</param>
    /// <param name="buffer">The bytes to write.</param>
    /// <returns>
    ///     A success when every byte was written, otherwise a failed result.
    /// </returns>
    Result Write(Address address, ReadOnlySpan<byte> buffer);
}
