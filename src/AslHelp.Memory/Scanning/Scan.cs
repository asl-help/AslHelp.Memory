using System;
using System.Buffers;
using System.Collections.Generic;

namespace AslHelp.Memory.Scanning;

/// <summary>
///     Provides pattern scans over an <see cref="IMemoryReader"/> or a buffer.
/// </summary>
/// <remarks>
///     Matches are yielded lazily in ascending order, so taking the first match stops the scan there. A chunk that
///     can't be read is skipped.
/// </remarks>
public static class Scan
{
    /// <summary>
    ///     The number of bytes read at a time.
    /// </summary>
    public const int ChunkSize = 64 * 1024;

    // The size a failed window read is split down to, because memory is readable per page.
    private const int PageSize = 4 * 1024;

    private static readonly MemoryRange _allMemory = new(Address.Zero, ulong.MaxValue);

    /// <summary>
    ///     Creates the step that searches the whole range for <paramref name="signature"/>.
    /// </summary>
    /// <param name="signature">The signature to search for, as in <c>48 8B ?? 05</c>.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="signature"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FormatException">
    ///     <paramref name="signature"/> isn't a pattern.
    /// </exception>
    public static ScanStep Create(string signature)
    {
        return Create(ScanPattern.Parse(signature));
    }

    /// <summary>
    ///     Creates the step that searches the whole range for <paramref name="pattern"/>.
    /// </summary>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    public static ScanStep Create(ScanPattern pattern)
    {
        return new(pattern, ScanStepKind.Range, 0, null);
    }

    /// <summary>
    ///     Searches <paramref name="range"/> for <paramref name="pattern"/>.
    /// </summary>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="range">The bytes to search.</param>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The addresses the pattern matches at.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/> is <see langword="null"/>.
    /// </exception>
    public static IEnumerable<Address> ScanRange(IMemoryReader reader, MemoryRange range, ScanPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return ScanChunks(reader, range, pattern);
    }

    /// <summary>
    ///     Searches <paramref name="buffer"/> for <paramref name="pattern"/>.
    /// </summary>
    /// <param name="buffer">The bytes to search.</param>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The offsets in <paramref name="buffer"/> the pattern matches at.
    /// </returns>
    public static IEnumerable<int> ScanBuffer(ReadOnlyMemory<byte> buffer, ScanPattern pattern)
    {
        if (pattern.Length == 0)
        {
            yield break;
        }

        for (int from = 0, match; (match = NextMatch(pattern, buffer.Span, ref from)) >= 0;)
        {
            yield return match;
        }
    }

    /// <summary>
    ///     Searches each of <paramref name="ranges"/> in turn for <paramref name="pattern"/>.
    /// </summary>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="ranges">The ranges to search.</param>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The addresses the pattern matches at.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/> or <paramref name="ranges"/> is <see langword="null"/>.
    /// </exception>
    public static IEnumerable<Address> ScanRanges(IMemoryReader reader, IEnumerable<MemoryRange> ranges, ScanPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(ranges);

        return RangesIterator(reader, ranges, pattern);
    }

    /// <summary>
    ///     Searches the readable regions of <paramref name="reader"/> for <paramref name="pattern"/>.
    /// </summary>
    /// <remarks>
    ///     Adjacent readable regions are searched as 1 range, so a match that spans 2 of them is found.
    /// </remarks>
    /// <typeparam name="TReader">The type of memory that reads bytes and lists its regions.</typeparam>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The addresses the pattern matches at on success, otherwise a failed result when the regions couldn't be
    ///     listed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/> is <see langword="null"/>.
    /// </exception>
    public static Result<IEnumerable<Address>> ScanRegions<TReader>(TReader reader, ScanPattern pattern)
        where TReader : IMemoryReader, IMemoryRegionProvider
    {
        ArgumentNullException.ThrowIfNull(reader);

        return ReadableRanges(reader, _allMemory).Map(ranges => RangesIterator(reader, ranges, pattern));
    }

    /// <summary>
    ///     Searches the readable regions of <paramref name="reader"/> inside <paramref name="range"/> for
    ///     <paramref name="pattern"/>.
    /// </summary>
    /// <remarks>
    ///     Each region is cut to <paramref name="range"/>, so a match is only found when all of its bytes are inside the
    ///     range. Adjacent readable regions are searched as 1 range, as in <see cref="ScanRegions{TReader}(TReader, ScanPattern)"/>.
    /// </remarks>
    /// <typeparam name="TReader">The type of memory that reads bytes and lists its regions.</typeparam>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="range">The bytes to search.</param>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The addresses the pattern matches at on success, otherwise a failed result when the regions couldn't be
    ///     listed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/> is <see langword="null"/>.
    /// </exception>
    public static Result<IEnumerable<Address>> ScanRegions<TReader>(TReader reader, MemoryRange range, ScanPattern pattern)
        where TReader : IMemoryReader, IMemoryRegionProvider
    {
        ArgumentNullException.ThrowIfNull(reader);

        return ReadableRanges(reader, range).Map(ranges => RangesIterator(reader, ClampIterator(ranges, range), pattern));
    }

    /// <summary>
    ///     Runs <paramref name="steps"/> over <paramref name="range"/>.
    /// </summary>
    /// <remarks>
    ///     The first step searches from the start of the range. Each later step searches around every address the
    ///     previous step returned, clamped to the range. An address outside the range ends that branch of the chain.
    /// </remarks>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="range">The bytes to search.</param>
    /// <param name="steps">The steps to run in order.</param>
    /// <returns>
    ///     The addresses the last step returns.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/> or <paramref name="steps"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="steps"/> is empty.
    /// </exception>
    public static IEnumerable<Address> ScanRange(IMemoryReader reader, MemoryRange range, params ScanStep[] steps)
    {
        return ScanRanges(reader, [range], steps);
    }

    /// <summary>
    ///     Runs <paramref name="steps"/> over each of <paramref name="ranges"/> in turn.
    /// </summary>
    /// <remarks>
    ///     Each range is a chain of its own, as <see cref="ScanRange(IMemoryReader, MemoryRange, ScanStep[])"/> runs it.
    /// </remarks>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="ranges">The ranges to search.</param>
    /// <param name="steps">The steps to run in order.</param>
    /// <returns>
    ///     The addresses the last step returns.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/>, <paramref name="ranges"/> or <paramref name="steps"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="steps"/> is empty.
    /// </exception>
    public static IEnumerable<Address> ScanRanges(IMemoryReader reader, IEnumerable<MemoryRange> ranges, params ScanStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Length == 0)
        {
            throw new ArgumentException(SR.ChainedScanNeedsStep, nameof(steps));
        }

        return StepsIterator(reader, ranges, steps);
    }

    /// <summary>
    ///     Runs <paramref name="steps"/> over the readable regions of <paramref name="reader"/>.
    /// </summary>
    /// <remarks>
    ///     Adjacent readable regions are searched as 1 range, so a match that spans 2 of them is found.
    /// </remarks>
    /// <typeparam name="TReader">The type of memory that reads bytes and lists its regions.</typeparam>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="steps">The steps to run in order.</param>
    /// <returns>
    ///     The addresses the last step returns on success, otherwise a failed result when the regions couldn't be
    ///     listed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/> or <paramref name="steps"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="steps"/> is empty.
    /// </exception>
    public static Result<IEnumerable<Address>> ScanRegions<TReader>(TReader reader, params ScanStep[] steps)
        where TReader : IMemoryReader, IMemoryRegionProvider
    {
        ArgumentNullException.ThrowIfNull(reader);

        var ranges = ReadableRanges(reader, _allMemory);
        if (ranges.IsErr)
        {
            return ranges.Error;
        }

        return Result.Ok(ScanRanges(reader, ranges.Unwrap(), steps));
    }

    /// <summary>
    ///     Runs <paramref name="step"/> as if the previous step matched at <paramref name="anchor"/>.
    /// </summary>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="range">The bytes a step without a window searches. A window is clamped to them.</param>
    /// <param name="step">The step to run.</param>
    /// <param name="anchor">The address the step's window starts or ends at, inside <paramref name="range"/>.</param>
    /// <returns>
    ///     The addresses the step returns.
    /// </returns>
    internal static IEnumerable<Address> ScanFrom(IMemoryReader reader, MemoryRange range, ScanStep step, Address anchor)
    {
        return ScanChain(reader, range, [step], 0, anchor);
    }

    private static Result<IEnumerable<MemoryRange>> ReadableRanges<TReader>(TReader reader, MemoryRange range)
        where TReader : IMemoryRegionProvider
    {
        var regions = reader.GetRegions(range);
        if (regions.IsErr)
        {
            return regions.Error;
        }

        return Result.Ok(regions.Unwrap().MergeReadable());
    }

    // Cuts with offsets from the start of each range, because End wraps to 0 for a range at the top of memory.
    private static IEnumerable<MemoryRange> ClampIterator(IEnumerable<MemoryRange> readable, MemoryRange range)
    {
        foreach (var merged in readable)
        {
            ulong skip = range.Start > merged.Start ? range.Start.Value - merged.Start.Value : 0;
            if (skip >= merged.Size)
            {
                continue;
            }

            Address start = new(merged.Start.Value + skip);
            ulong offset = start.Value - range.Start.Value;
            if (offset >= range.Size)
            {
                continue;
            }

            yield return new(start, Math.Min(merged.Size - skip, range.Size - offset));
        }
    }

    private static IEnumerable<Address> RangesIterator(IMemoryReader reader, IEnumerable<MemoryRange> ranges, ScanPattern pattern)
    {
        foreach (var range in ranges)
        {
            foreach (var match in ScanChunks(reader, range, pattern))
            {
                yield return match;
            }
        }
    }

    private static IEnumerable<Address> StepsIterator(IMemoryReader reader, IEnumerable<MemoryRange> ranges, ScanStep[] steps)
    {
        foreach (var range in ranges)
        {
            foreach (var match in ScanChain(reader, range, steps, 0, range.Start))
            {
                yield return match;
            }
        }
    }

    private static IEnumerable<Address> ScanChain(IMemoryReader reader, MemoryRange range, ScanStep[] steps, int index, Address anchor)
    {
        var step = steps[index];
        bool isLast = index == steps.Length - 1;

        var window = step.Clamp(range, anchor);
        var matches = step.Kind == ScanStepKind.Range
            ? ScanChunks(reader, window, step.Pattern)
            : ScanInOneRead(reader, window, step.Pattern);

        foreach (var match in matches)
        {
            var address = match;
            if (step.Resolver is not null && !step.Resolver(reader, match).TryUnwrap(out address))
            {
                continue;
            }

            if (isLast)
            {
                yield return address;
                continue;
            }

            if (!range.Contains(address))
            {
                continue;
            }

            foreach (var next in ScanChain(reader, range, steps, index + 1, address))
            {
                yield return next;
            }
        }
    }

    // A remote read is a syscall, so the window is read once into 1 rented array that lives across the yields.
    private static IEnumerable<Address> ScanInOneRead(IMemoryReader reader, MemoryRange window, ScanPattern pattern)
    {
        int size = (int)window.Size;
        if (size < pattern.Length || pattern.Length == 0)
        {
            yield break;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(size);

        try
        {
            if (reader.Read(window.Start, buffer.AsSpan(0, size)).IsOk)
            {
                for (int from = 0, match; (match = NextMatch(pattern, buffer.AsSpan(0, size), ref from)) >= 0;)
                {
                    yield return window.Start + match;
                }

                yield break;
            }

            foreach (var (partStart, partEnd) in ReadParts(reader, window, buffer))
            {
                for (int from = partStart, match; (match = NextMatch(pattern, buffer.AsSpan(0, partEnd), ref from)) >= 0;)
                {
                    yield return window.Start + match;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Reads the readable parts of a window whose 1 read failed, each as offsets into the buffer in ascending order.
    // Asks for the regions when the reader lists them, because that costs 1 call. Splits the failed read at page borders
    // otherwise.
    private static List<(int Start, int End)> ReadParts(IMemoryReader reader, MemoryRange window, byte[] buffer)
    {
        List<(int Start, int End)> parts = [];

        if (reader is IMemoryRegionProvider provider && ReadableRanges(provider, window).TryUnwrap(out var readable))
        {
            foreach (var range in ClampIterator(readable, window))
            {
                int from = (int)(range.Start.Value - window.Start.Value);
                ReadOrSplit(reader, window.Start, buffer, from, from + (int)range.Size, parts);
            }

            return parts;
        }

        SplitPart(reader, window.Start, buffer, 0, (int)window.Size, parts);
        return parts;
    }

    // Adds the part when it reads and splits it otherwise. Joins a part to the one before when they touch, so a match
    // across both is found.
    private static void ReadOrSplit(IMemoryReader reader, Address start, byte[] buffer, int from, int to, List<(int Start, int End)> parts)
    {
        if (reader.Read(start + from, buffer.AsSpan(from, to - from)).IsErr)
        {
            SplitPart(reader, start, buffer, from, to, parts);
            return;
        }

        if (parts.Count > 0 && parts[^1].End == from)
        {
            parts[^1] = (parts[^1].Start, to);
        }
        else
        {
            parts.Add((from, to));
        }
    }

    // Splits a part that failed to read at the page border nearest its middle, because memory is readable per page. A
    // part inside 1 page is left out.
    private static void SplitPart(IMemoryReader reader, Address start, byte[] buffer, int from, int to, List<(int Start, int End)> parts)
    {
        // Page borders sit at the offsets where offset + misalign is a multiple of PageSize.
        long misalign = (long)(start.Value & (PageSize - 1));
        long middle = from + ((to - from) / 2);
        long border = ((middle + misalign) / PageSize * PageSize) - misalign;
        if (border <= from)
        {
            border += PageSize;
        }

        if (border >= to)
        {
            return;
        }

        ReadOrSplit(reader, start, buffer, from, (int)border, parts);
        ReadOrSplit(reader, start, buffer, (int)border, to, parts);
    }

    // Returns the offset of the next match at or after from and moves from past it. Returns -1 when no match is left.
    private static int NextMatch(ScanPattern pattern, ReadOnlySpan<byte> data, ref int from)
    {
        if (data.Length - from < pattern.Length)
        {
            return -1;
        }

        int hit = pattern.IndexOf(data[from..]);
        if (hit < 0)
        {
            from = data.Length;
            return -1;
        }

        int match = from + hit;
        from = match + 1;
        return match;
    }

    private static IEnumerable<Address> ScanChunks(IMemoryReader reader, MemoryRange range, ScanPattern pattern)
    {
        int length = pattern.Length;
        if (length == 0 || range.Size < (ulong)length)
        {
            yield break;
        }

        // Each chunk repeats the last bytes of the one before it, 1 fewer than the pattern spans. A match across the
        // seam is then whole in exactly 1 chunk. A match that ends in the repeated bytes is found only in the later one.
        int overlap = length - 1;
        int bufferSize = (int)Math.Min(range.Size, (ulong)Math.Max(ChunkSize, 2L * length));

        // 1 rented buffer serves every chunk of the range, so the scan allocates no array per chunk.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            for (ulong offset = 0; ;)
            {
                int taken = (int)Math.Min((ulong)bufferSize, range.Size - offset);
                var chunkStart = new Address(unchecked(range.Start.Value + offset));

                if (reader.Read(chunkStart, buffer.AsSpan(0, taken)).IsOk)
                {
                    for (int from = 0, match; (match = NextMatch(pattern, buffer.AsSpan(0, taken), ref from)) >= 0;)
                    {
                        yield return chunkStart + match;
                    }
                }

                if (offset + (ulong)taken >= range.Size)
                {
                    yield break;
                }

                offset += (ulong)(taken - overlap);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
