using System.Collections.Generic;

namespace AslHelp.Memory;

/// <summary>
///     Provides the committed regions of a target's memory.
/// </summary>
public interface IMemoryRegionProvider
{
    /// <summary>
    ///     Gets the committed regions that overlap <paramref name="range"/>.
    /// </summary>
    /// <param name="range">The bytes to find regions for.</param>
    /// <returns>
    ///     The regions in ascending order on success, otherwise a failed result.
    /// </returns>
    Result<IReadOnlyList<MemoryRegion>> GetRegions(MemoryRange range);
}
