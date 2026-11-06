namespace AslHelp.Memory.Remote.Errors;

/// <summary>
///     Represents bytes of another process that couldn't be read.
/// </summary>
/// <remarks>
///     Formats the message on access, because a read that fails is normal, can run every tick and rarely reads it.
/// </remarks>
/// <param name="Address">The address of the first byte.</param>
/// <param name="Size">The number of bytes.</param>
internal sealed record RemoteUnreadableError(Address Address, int Size) : ResultError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatUnreadable(Size, Address);
}
