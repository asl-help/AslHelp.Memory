using System;
using System.Buffers;
using System.Collections.Generic;

namespace AslHelp.Memory.Scanning;

/// <summary>
///     Provides pattern scans over an <see cref="IMemoryReader"/>.
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
    public static ScanStep For(string signature)
    {
        return For(ScanPattern.Parse(signature));
    }

    /// <summary>
    ///     Creates the step that searches the whole range for <paramref name="pattern"/>.
    /// </summary>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    public static ScanStep For(ScanPattern pattern)
    {
        return new(pattern, ScanStepKind.Range, 0, null);
    }

    /// <summary>
    ///     Creates a window of <paramref name="size"/> bytes that starts at the previous step's match.
    /// </summary>
    /// <param name="size">The number of bytes to search.</param>
    /// <returns>
    ///     The window, which <see cref="ScanWindow.For(string)"/> turns into a step.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="size"/> is negative.
    /// </exception>
    public static ScanWindow Forward(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        return new(ScanStepKind.Forward, size);
    }

    /// <summary>
    ///     Creates a window of <paramref name="size"/> bytes that ends at the previous step's match.
    /// </summary>
    /// <param name="size">The number of bytes to search.</param>
    /// <returns>
    ///     The window, which <see cref="ScanWindow.For(string)"/> turns into a step.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="size"/> is negative.
    /// </exception>
    public static ScanWindow Backward(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        return new(ScanStepKind.Backward, size);
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
    public static IEnumerable<Address> Range(IMemoryReader reader, MemoryRange range, ScanPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return Chunked(reader, range, pattern);
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
    public static IEnumerable<Address> Ranges(IMemoryReader reader, IEnumerable<MemoryRange> ranges, ScanPattern pattern)
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
    public static Result<IEnumerable<Address>> Regions<TReader>(TReader reader, ScanPattern pattern)
        where TReader : IMemoryReader, IMemoryRegionProvider
    {
        ArgumentNullException.ThrowIfNull(reader);

        return ReadableRanges(reader).Map(ranges => RangesIterator(reader, ranges, pattern));
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
    public static IEnumerable<Address> Range(IMemoryReader reader, MemoryRange range, params ScanStep[] steps)
    {
        return Ranges(reader, [range], steps);
    }

    /// <summary>
    ///     Runs <paramref name="steps"/> over each of <paramref name="ranges"/> in turn.
    /// </summary>
    /// <remarks>
    ///     Each range is a chain of its own, as <see cref="Range(IMemoryReader, MemoryRange, ScanStep[])"/> runs it.
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
    public static IEnumerable<Address> Ranges(IMemoryReader reader, IEnumerable<MemoryRange> ranges, params ScanStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Length == 0)
        {
            throw new ArgumentException("A chained scan needs at least 1 step.", nameof(steps));
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
    public static Result<IEnumerable<Address>> Regions<TReader>(TReader reader, params ScanStep[] steps)
        where TReader : IMemoryReader, IMemoryRegionProvider
    {
        ArgumentNullException.ThrowIfNull(reader);

        var ranges = ReadableRanges(reader);
        if (ranges.IsErr)
        {
            return ranges.Error;
        }

        return Result.Ok(Ranges(reader, ranges.Unwrap(), steps));
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
    internal static IEnumerable<Address> From(IMemoryReader reader, MemoryRange range, ScanStep step, Address anchor)
    {
        return Fold(reader, range, [step], 0, anchor);
    }

    private static Result<IEnumerable<MemoryRange>> ReadableRanges<TReader>(TReader reader)
        where TReader : IMemoryRegionProvider
    {
        var regions = reader.GetRegions(new(Address.Zero, ulong.MaxValue));
        if (regions.IsErr)
        {
            return regions.Error;
        }

        return Result.Ok(regions.Unwrap().MergeReadable());
    }

    private static IEnumerable<Address> RangesIterator(IMemoryReader reader, IEnumerable<MemoryRange> ranges, ScanPattern pattern)
    {
        foreach (var range in ranges)
        {
            foreach (var match in Chunked(reader, range, pattern))
            {
                yield return match;
            }
        }
    }

    private static IEnumerable<Address> StepsIterator(IMemoryReader reader, IEnumerable<MemoryRange> ranges, ScanStep[] steps)
    {
        foreach (var range in ranges)
        {
            foreach (var match in Fold(reader, range, steps, 0, range.Start))
            {
                yield return match;
            }
        }
    }

    private static IEnumerable<Address> Fold(IMemoryReader reader, MemoryRange range, ScanStep[] steps, int index, Address anchor)
    {
        var step = steps[index];
        bool isLast = index == steps.Length - 1;

        var scope = step.Scope(range, anchor);
        var matches = step.Kind == ScanStepKind.Range
            ? Chunked(reader, scope, step.Pattern)
            : Windowed(reader, scope, step.Pattern);

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

            foreach (var next in Fold(reader, range, steps, index + 1, address))
            {
                yield return next;
            }
        }
    }

    // A remote read is a syscall, so the window is read once into 1 rented array that lives across the yields.
    private static IEnumerable<Address> Windowed(IMemoryReader reader, MemoryRange window, ScanPattern pattern)
    {
        int size = (int)window.Size;
        if (size < pattern.Length || pattern.Length == 0)
        {
            yield break;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(size);

        try
        {
            if (reader.Read(window.Start, buffer.AsSpan(0, size)).IsErr)
            {
                yield break;
            }

            for (int from = 0; ;)
            {
                int hit = pattern.IndexOf(buffer.AsSpan(from, size - from));
                if (hit < 0)
                {
                    yield break;
                }

                from += hit;
                yield return window.Start + from;
                from++;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IEnumerable<Address> Chunked(IMemoryReader reader, MemoryRange range, ScanPattern pattern)
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
                    for (int from = 0; ;)
                    {
                        int hit = pattern.IndexOf(buffer.AsSpan(from, taken - from));
                        if (hit < 0)
                        {
                            break;
                        }

                        from += hit;
                        yield return chunkStart + from;
                        from++;
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
