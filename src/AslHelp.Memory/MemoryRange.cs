namespace AslHelp.Memory;

/// <summary>
///     Represents a contiguous range of bytes in a target's memory.
/// </summary>
/// <param name="Start">The address of the first byte.</param>
/// <param name="Size">The number of bytes.</param>
public readonly record struct MemoryRange(Address Start, ulong Size)
{
    /// <summary>
    ///     Gets the address one past the last byte.
    /// </summary>
    public Address End => new(unchecked(Start.Value + Size));

    /// <summary>
    ///     Determines whether <paramref name="address"/> lies inside the range.
    /// </summary>
    /// <param name="address">The address to check.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="address"/> is at or above <see cref="Start"/> and below
    ///     <see cref="End"/>.
    /// </returns>
    public bool Contains(Address address)
    {
        return address >= Start && address.Value - Start.Value < Size;
    }
}
