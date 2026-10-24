namespace AslHelp.Memory.Scanning.Errors;

/// <summary>
///     Represents a scan match that couldn't be resolved to an address.
/// </summary>
internal sealed record ScanError : ResultError
{
    private ScanError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a pointer size that a relative reference can't be followed at.
    /// </summary>
    /// <param name="pointerSize">The pointer size of the layout.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static ScanError UnsupportedPointerSize(byte pointerSize)
    {
        return new($"A relative reference is followed at a pointer size of 4 or 8 bytes, not {pointerSize}.");
    }
}
