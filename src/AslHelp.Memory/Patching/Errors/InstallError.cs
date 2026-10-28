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
        return new($"The pool at {pool} is out of a relative jump's reach of {entry}.");
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
        return new($"The pool at {pool} has no slot left.");
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
        return new($"The stub for {entry} is out of reach of a relative jump.");
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
        return new($"A thread stands inside the instructions at {entry}. It's left alone and the next attempt asks again.");
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
        return new($"Not every thread of the target could be suspended and read, so nothing is claimed about {entry}.");
    }
}
