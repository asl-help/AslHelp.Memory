using System;
using System.Collections.Generic;

namespace AslHelp.Memory;

/// <summary>
///     Provides merges over sequences of <see cref="MemoryRegion"/>.
/// </summary>
public static class MemoryRegionExtensions
{
    extension(IEnumerable<MemoryRegion> regions)
    {
        /// <summary>
        ///     Joins adjacent readable regions into contiguous ranges.
        /// </summary>
        /// <remarks>
        ///     The regions must be in ascending order without overlaps, as
        ///     <see cref="IMemoryRegionProvider.GetRegions(MemoryRange)"/> returns them. A gap between 2 regions ends a
        ///     range. A region that can't be read also ends a range and is left out.
        /// </remarks>
        /// <returns>
        ///     The readable ranges in ascending order.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The regions are <see langword="null"/>.
        /// </exception>
        public IEnumerable<MemoryRange> MergeReadable()
        {
            ArgumentNullException.ThrowIfNull(regions);

            return MergeReadableIterator(regions);
        }
    }

    private static IEnumerable<MemoryRange> MergeReadableIterator(IEnumerable<MemoryRegion> regions)
    {
        MemoryRange current = default;

        foreach (var region in regions)
        {
            if ((region.Protection & MemoryProtection.Read) == 0)
            {
                if (current.Size > 0)
                {
                    yield return current;
                    current = default;
                }

                continue;
            }

            if (current.Size > 0 && region.Range.Start == current.End)
            {
                current = current with { Size = current.Size + region.Range.Size };
                continue;
            }

            if (current.Size > 0)
            {
                yield return current;
            }

            current = region.Range;
        }

        if (current.Size > 0)
        {
            yield return current;
        }
    }
}
