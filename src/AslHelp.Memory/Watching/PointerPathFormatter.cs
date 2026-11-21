using System.Text;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Formats pointer paths for the memory watchers.
/// </summary>
internal static class PointerPathFormatter
{
    /// <summary>
    ///     Formats a pointer path as its address followed by each offset, as in <c>0x1000 0x10 -0x8</c>.
    /// </summary>
    /// <param name="address">The address the path starts at.</param>
    /// <param name="offsets">The offsets after each pointer read.</param>
    /// <returns>
    ///     The formatted path.
    /// </returns>
    public static string Format(Address address, long[] offsets)
    {
        StringBuilder path = new(address.ToString());
        foreach (long offset in offsets)
        {
            // Negates in unchecked ulong, because negating long.MinValue overflows.
            ulong magnitude = offset < 0 ? unchecked(0UL - (ulong)offset) : (ulong)offset;
            path.Append(offset < 0 ? " -" : " ").Append(new Address(magnitude));
        }

        return path.ToString();
    }
}
