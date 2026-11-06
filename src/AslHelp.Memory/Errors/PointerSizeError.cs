namespace AslHelp.Memory.Errors;

/// <summary>
///     Represents a read in a layout whose pointer size is outside 1 to 8 bytes.
/// </summary>
/// <remarks>
///     Formats the message on access, because a read that fails is normal, can run every tick and rarely reads it.
/// </remarks>
/// <param name="PointerSize">The pointer size of the layout.</param>
internal sealed record PointerSizeError(byte PointerSize) : ResultError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatPointerSizeOutOfRange(PointerSize);
}
