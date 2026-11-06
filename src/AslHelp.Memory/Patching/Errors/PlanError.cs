namespace AslHelp.Memory.Patching.Errors;

/// <summary>
///     Represents an entry that can't be patched.
/// </summary>
internal sealed record PlanError : ResultError
{
    private PlanError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a target whose pointer size isn't an Intel one.
    /// </summary>
    /// <param name="pointerSize">The target's pointer size.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PlanError UnsupportedArchitecture(int pointerSize)
    {
        return new(SR.FormatPlanUnsupportedArchitecture(pointerSize));
    }

    /// <summary>
    ///     Creates the error for an entry whose bytes couldn't be read.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PlanError UnreadableEntry(Address entry)
    {
        return new(SR.FormatPlanUnreadableEntry(entry));
    }

    /// <summary>
    ///     Creates the error for an entry whose first 5 bytes straddle 2 8-byte words.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PlanError EntryStraddlesWord(Address entry)
    {
        return new(SR.FormatPlanEntryStraddlesWord(entry));
    }

    /// <summary>
    ///     Creates the error for an entry whose first instructions couldn't be decoded.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PlanError UnrecognizedPrologue(Address entry)
    {
        return new(SR.FormatPlanUnrecognizedPrologue(entry));
    }

    /// <summary>
    ///     Creates the error for an entry whose displaced instructions depend on their address.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PlanError PositionDependentEntry(Address entry)
    {
        return new(SR.FormatPlanPositionDependentEntry(entry));
    }

    /// <summary>
    ///     Creates the error for a stub that doesn't fit its slot.
    /// </summary>
    /// <param name="entry">The entry the stub is for.</param>
    /// <param name="wants">The size of the stub.</param>
    /// <param name="room">The size a slot has for a stub.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PlanError StubTooLarge(Address entry, int wants, int room)
    {
        return new(SR.FormatPlanStubTooLarge(entry, wants, room));
    }
}
