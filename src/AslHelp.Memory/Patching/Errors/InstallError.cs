namespace AslHelp.Memory.Patching.Errors;

/// <summary>
///     Represents a planned patch that couldn't be installed or restored.
/// </summary>
internal sealed record InstallError : ResultError
{
    private InstallError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a pool too far from an entry for a relative jump.
    /// </summary>
    /// <param name="pool">The pool's base.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static InstallError PoolOutOfReach(Address pool, Address entry)
    {
        return new(SR.FormatInstallPoolOutOfReach(pool, entry));
    }

    /// <summary>
    ///     Creates the error for a pool with every slot handed out.
    /// </summary>
    /// <param name="pool">The pool's base.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static InstallError PoolFull(Address pool)
    {
        return new(SR.FormatInstallPoolFull(pool));
    }

    /// <summary>
    ///     Creates the error for a stub too far from its entry for a relative jump.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static InstallError StubOutOfReach(Address entry)
    {
        return new(SR.FormatInstallStubOutOfReach(entry));
    }

    /// <summary>
    ///     Creates the error for a thread stopped inside the bytes a write would replace.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static InstallError ThreadInside(Address entry)
    {
        return new(SR.FormatInstallThreadInside(entry));
    }

    /// <summary>
    ///     Creates the error for a suspension that couldn't stop or read every thread.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static InstallError ThreadsNotSuspended(Address entry)
    {
        return new(SR.FormatInstallThreadsNotSuspended(entry));
    }
}
