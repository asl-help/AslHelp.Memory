namespace AslHelp.Memory.Remote.Errors;

/// <summary>
///     Represents a failed write or module listing of another process.
/// </summary>
internal sealed record RemoteMemoryError : ResultError
{
    private RemoteMemoryError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for <paramref name="size"/> bytes at <paramref name="address"/> that couldn't be written.
    /// </summary>
    /// <param name="address">The address of the first byte.</param>
    /// <param name="size">The number of bytes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemoteMemoryError Unwritable(Address address, int size)
    {
        return new(SR.FormatUnwritable(size, address));
    }

    /// <summary>
    ///     Creates the error for a module list that couldn't be read from the process.
    /// </summary>
    /// <param name="reason">The system message for the failure.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemoteMemoryError ModulesUnavailable(string reason)
    {
        return new(SR.FormatModulesUnavailable(reason));
    }
}
