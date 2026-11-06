namespace AslHelp.Memory.Remote.Errors;

/// <summary>
///     Represents a failed reservation, protection change, code write or thread suspension in another process.
/// </summary>
internal sealed record RemotePatchError : ResultError
{
    private RemotePatchError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a process asked to suspend itself, which would stop the asking thread with nothing left
    ///     to resume it.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError CannotSuspendItself()
    {
        return new(SR.PatchCannotSuspendItself);
    }

    /// <summary>
    ///     Creates the error for a thread snapshot the system would not give.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError ThreadsUnlisted()
    {
        return new(SR.PatchThreadsUnlisted);
    }

    /// <summary>
    ///     Creates the error for a snapshot that listed no threads, which no running process has.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError NoThreads()
    {
        return new(SR.PatchNoThreads);
    }

    /// <summary>
    ///     Creates the error for a call through a handle already released.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError HandleReleased()
    {
        return new(SR.PatchHandleReleased);
    }

    /// <summary>
    ///     Creates the error for an address above what a 32-bit host can point at.
    /// </summary>
    /// <param name="address">The address the host cannot point at.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError Unaddressable(Address address)
    {
        return new(SR.FormatPatchUnaddressable(address));
    }

    /// <summary>
    ///     Creates the error for an anchor with no free memory within a relative jump.
    /// </summary>
    /// <param name="anchor">The address the reservation had to be reachable from.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError OutOfReach(Address anchor)
    {
        return new(SR.FormatPatchOutOfReach(anchor));
    }

    /// <summary>
    ///     Creates the error for a protection change the system refused.
    /// </summary>
    /// <param name="address">The first byte of the range.</param>
    /// <param name="reason">The system message for the failure.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError NotProtected(Address address, string reason)
    {
        return new(SR.FormatPatchNotProtected(address, reason));
    }

    /// <summary>
    ///     Creates the error for a reservation from <see cref="RemotePatchWriter.Reserve"/> the system would not
    ///     release.
    /// </summary>
    /// <param name="page">The reservation's address.</param>
    /// <param name="reason">The system message for the failure.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError NotReleased(Address page, string reason)
    {
        return new(SR.FormatPatchNotReleased(page, reason));
    }

    /// <summary>
    ///     Creates the error for a range the system would not make executable, readable and writable.
    /// </summary>
    /// <param name="address">The first byte of the range.</param>
    /// <param name="reason">The system message for the failure.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError NotMadeExecutable(Address address, string reason)
    {
        return new(SR.FormatPatchNotMadeExecutable(address, reason));
    }

    /// <summary>
    ///     Creates the error for a write the system refused on a range already executable and writable.
    /// </summary>
    /// <param name="address">The first byte of the range.</param>
    /// <param name="reason">The system message for the failure.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError CodeNotWritten(Address address, string reason)
    {
        return new(SR.FormatPatchCodeNotWritten(address, reason));
    }

    /// <summary>
    ///     Creates the error for a write the system accepted but cut short.
    /// </summary>
    /// <param name="address">The first byte of the range.</param>
    /// <param name="wanted">The number of bytes asked for.</param>
    /// <param name="written">The number of bytes the system wrote.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError CodePartlyWritten(Address address, int wanted, nuint written)
    {
        return new(SR.FormatPatchCodePartlyWritten(written, wanted, address));
    }
}
