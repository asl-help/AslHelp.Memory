using System;

namespace AslHelp.Memory.Scanning;

/// <summary>
///     Represents 1 step of a chained scan: a pattern to search for, an optional window around the previous step's
///     match and an optional resolver for each match.
/// </summary>
/// <remarks>
///     Create one with <see cref="Scan.Create(string)"/> or with <see cref="ScanWindow.Create(string)"/> on a
///     <see cref="ScanWindow.Forward(int)"/> or <see cref="ScanWindow.Backward(int)"/> window.
/// </remarks>
public readonly record struct ScanStep
{
    internal ScanStep(ScanPattern pattern, ScanStepKind kind, int windowSize, AddressResolver? resolver)
    {
        Pattern = pattern;
        Kind = kind;
        WindowSize = windowSize;
        Resolver = resolver;
    }

    /// <summary>
    ///     Gets the pattern the step searches for.
    /// </summary>
    public ScanPattern Pattern { get; }

    internal ScanStepKind Kind { get; }

    internal int WindowSize { get; }

    internal AddressResolver? Resolver { get; }

    /// <summary>
    ///     Creates a copy of this step that turns each match into the address <paramref name="resolver"/> returns.
    /// </summary>
    /// <remarks>
    ///     A match whose resolver fails is dropped.
    /// </remarks>
    /// <param name="resolver">The resolver to apply to each match, such as <c>AddressResolver.FromRelative(3)</c>.</param>
    /// <returns>
    ///     The step with the resolver.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="resolver"/> is <see langword="null"/>.
    /// </exception>
    public ScanStep WithResolver(AddressResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new(Pattern, Kind, WindowSize, resolver);
    }

    /// <summary>
    ///     Clamps the step's window to the bytes of <paramref name="range"/> around <paramref name="anchor"/>.
    /// </summary>
    /// <param name="range">The range the chain scans.</param>
    /// <param name="anchor">The previous step's match. The first step uses the start of the range.</param>
    /// <returns>
    ///     The bytes to search, clamped to <paramref name="range"/>.
    /// </returns>
    internal MemoryRange Clamp(MemoryRange range, Address anchor)
    {
        ulong offset = anchor.Value - range.Start.Value;
        ulong size = (ulong)WindowSize;

        return Kind switch
        {
            ScanStepKind.Forward => new(anchor, Math.Min(size, range.Size - offset)),
            ScanStepKind.Backward => size >= offset
                ? new(range.Start, offset)
                : new(new Address(anchor.Value - size), size),
            _ => range,
        };
    }
}
