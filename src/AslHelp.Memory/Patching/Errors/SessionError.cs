namespace AslHelp.Memory.Patching.Errors;

/// <summary>
///     Represents a patch session's refusal of an install before it planned anything.
/// </summary>
internal sealed record SessionError : ResultError
{
    private SessionError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for an install asked of a session that was disposed.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static SessionError Closed()
    {
        return new("The patch session is closed, so nothing more is written into the target.");
    }

    /// <summary>
    ///     Creates the error for an entry at address 0, which no compiled method begins at.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static SessionError NoCodeToRedirect()
    {
        return new("There is no compiled code at that address to redirect, so there is nothing to hook.");
    }
}
