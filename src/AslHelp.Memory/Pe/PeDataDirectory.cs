namespace AslHelp.Memory.Pe;

/// <summary>
///     Represents an entry of the data directory in a module's PE optional header.
/// </summary>
/// <remarks>
///     An entry with a <see cref="VirtualAddress"/> of 0 means the module doesn't have that table.
/// </remarks>
/// <param name="VirtualAddress">The offset of the table from the module's base address.</param>
/// <param name="Size">The number of bytes the table spans.</param>
public readonly record struct PeDataDirectory(
    uint VirtualAddress,
    uint Size)
{
    /// <summary>
    ///     Determines whether <paramref name="rva"/> lies inside the table.
    /// </summary>
    /// <param name="rva">The offset from the module's base address.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="rva"/> is at or above <see cref="VirtualAddress"/> and below the
    ///     end of the table.
    /// </returns>
    public bool Contains(uint rva)
    {
        return rva >= VirtualAddress && rva - VirtualAddress < Size;
    }
}
