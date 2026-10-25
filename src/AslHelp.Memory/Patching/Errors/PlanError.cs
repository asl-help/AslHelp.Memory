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
    public static PlanError NotIntel(int pointerSize)
    {
        return new($"A target with {pointerSize}-byte pointers can't be patched. Patching covers x86 and x64.");
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
        return new($"The code at {entry} couldn't be read.");
    }

    /// <summary>
    ///     Creates the error for an entry whose first 5 bytes cross an 8-byte boundary.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PlanError StraddlesAWord(Address entry)
    {
        return new($"The entry at {entry} crosses an 8-byte boundary, so the jump can't be written in 1 store.");
    }

    /// <summary>
    ///     Creates the error for an entry whose first instructions couldn't be decoded.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PlanError UnrecognisedPrologue(Address entry)
    {
        return new($"The instructions at {entry} couldn't be decoded, so the jump could land inside 1 of them.");
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
        return new($"The instructions at {entry} depend on their address, so they can't be moved into a stub.");
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
        return new($"The stub for {entry} is {wants} bytes, and a slot has room for {room}. Capture less.");
    }
}
