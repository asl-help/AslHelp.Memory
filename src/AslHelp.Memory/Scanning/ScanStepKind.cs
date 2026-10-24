namespace AslHelp.Memory.Scanning;

/// <summary>
///     Specifies the bytes a <see cref="ScanStep"/> searches.
/// </summary>
internal enum ScanStepKind
{
    /// <summary>
    ///     The step searches the whole range.
    /// </summary>
    Range,

    /// <summary>
    ///     The step searches a window that starts at the previous match.
    /// </summary>
    Forward,

    /// <summary>
    ///     The step searches a window that ends at the previous match.
    /// </summary>
    Backward,
}
