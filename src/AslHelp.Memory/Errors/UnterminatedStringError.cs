namespace AslHelp.Memory.Errors;

/// <summary>
///     Represents a string with no terminator within the bound a read searched.
/// </summary>
/// <remarks>
///     Formats the message on access, because a read that fails is normal, can run every tick and rarely reads it.
/// </remarks>
/// <param name="Address">The address of the string.</param>
/// <param name="MaxLength">The bound the read searched within.</param>
internal sealed record UnterminatedStringError(Address Address, int MaxLength) : ResultError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatTerminatorNotFound(Address, MaxLength);
}
