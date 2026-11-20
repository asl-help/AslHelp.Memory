namespace AslHelp.Memory.Remote.Errors;

/// <summary>
///     Represents a failed call on another process, like a write, a module listing or any call after the handle closed.
/// </summary>
internal sealed record RemoteMemoryError : ResultError
{
    // Keep 1 instance, because a watcher can read a disposed instance every tick.
    private static readonly RemoteMemoryError _processClosed = new(SR.ProcessClosed);

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
    ///     Gets the error for a call after <see cref="RemoteProcessMemory.Dispose"/>.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemoteMemoryError ProcessClosed()
    {
        return _processClosed;
    }

    /// <summary>
    ///     Creates the error for a process that refused a handle with the rights to write.
    /// </summary>
    /// <param name="reason">The system message for the failure.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemoteMemoryError WriteHandleRefused(string reason)
    {
        return new(SR.FormatWriteHandleRefused(reason));
    }

    /// <summary>
    ///     Creates the error for a region query Windows refused.
    /// </summary>
    /// <param name="address">The address of the refused query.</param>
    /// <param name="reason">The system message for the failure.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemoteMemoryError RegionsUnavailable(Address address, string reason)
    {
        return new(SR.FormatRegionsUnavailable(address, reason));
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

    /// <summary>
    ///     Creates the error for a process whose module list doesn't start with a module Windows can describe.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemoteMemoryError MainModuleNotListed()
    {
        return new(SR.MainModuleNotListed);
    }
}
