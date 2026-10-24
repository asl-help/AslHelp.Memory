namespace AslHelp.Memory.Scanning;

/// <summary>
///     Represents a window around the previous step's match that still needs a pattern.
/// </summary>
/// <remarks>
///     Create one with <see cref="Scan.Forward(int)"/> or <see cref="Scan.Backward(int)"/>.
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
    ///     Creates the step that searches the window for <paramref name="signature"/>.
    /// </summary>
    /// <param name="signature">The signature to search for, as in <c>48 8B ?? 05</c>.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    ///     <paramref name="signature"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.FormatException">
    ///     <paramref name="signature"/> isn't a pattern.
    /// </exception>
    public ScanStep For(string signature)
    {
        return For(ScanPattern.Parse(signature));
    }

    /// <summary>
    ///     Creates the step that searches the window for <paramref name="pattern"/>.
    /// </summary>
    /// <param name="pattern">The pattern to search for.</param>
    /// <returns>
    ///     The step.
    /// </returns>
    public ScanStep For(ScanPattern pattern)
    {
        return new(pattern, _kind, _size, null);
    }
}
