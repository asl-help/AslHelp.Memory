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
        return new("A process cannot suspend its own threads.");
    }

    /// <summary>
    ///     Creates the error for a thread snapshot the system would not give.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError ThreadsUnlisted()
    {
        return new("The target's threads could not be listed.");
    }

    /// <summary>
    ///     Creates the error for a snapshot that listed no threads, which no running process has.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError NoThreads()
    {
        return new("The target's thread list was empty.");
    }

    /// <summary>
    ///     Creates the error for a call through a handle already released.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static RemotePatchError HandleReleased()
    {
        return new("The patch handle has been released.");
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
        return new($"0x{(ulong)address:X} is above what a 32-bit process can address.");
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
        return new($"No memory within reach of 0x{(ulong)anchor:X} was free, so nothing there can be hooked.");
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
        return new($"The protection at 0x{(ulong)address:X} could not be changed: {reason}");
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
        return new($"The reservation at 0x{(ulong)page:X} could not be released: {reason}");
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
        return new($"0x{(ulong)address:X} could not be made executable: {reason}");
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
        return new($"The code at 0x{(ulong)address:X} could not be written: {reason}");
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
        return new($"Only {written} of {wanted} bytes were written at 0x{(ulong)address:X}.");
    }
}
