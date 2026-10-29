using System;

namespace AslHelp.Memory.Scanning;

/// <summary>
///     Represents a window around the previous step's match that still needs a pattern.
/// </summary>
/// <remarks>
///     Create one with <see cref="Forward(int)"/> or <see cref="Backward(int)"/>.
/// </remarks>
public readonly record struct ScanWindow
{
    private readonly ScanStepKind _kind;
    private readonly int _size;

    internal ScanWindow(ScanStepKind kind, int size)
    {
        _kind = kind;
        _size = size;
    }

    /// <summary>
    ///     Creates a window of <paramref name="size"/> bytes that starts at the previous step's match.
    /// </summary>
    /// <param name="size">The number of bytes to search.</param>
    /// <returns>
    ///     The window, which <see cref="Create(string)"/> turns into a step.
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
    ///     The window, which <see cref="Create(string)"/> turns into a step.
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
    ///     Creates the step that searches the window for <paramref name="signature"/>.
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
    public ScanStep Create(string signature)
    {
        return Create(ScanPattern.Parse(signature));
    }

    /// <summary>
    ///     Creates the step that searches the window for <paramref name="pattern"/>.
    /// </summary>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    public ScanStep Create(ScanPattern pattern)
    {
        return new(pattern, _kind, _size, null);
    }
}
