namespace AslHelp.Memory.Patching.Errors;

/// <summary>
///     Represents a patch that couldn't be planned, composed or read.
/// </summary>
internal sealed record PatchError : ResultError
{
    private PatchError(string message)
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
    public static PatchError NotIntel(int pointerSize)
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
    public static PatchError UnreadableEntry(Address entry)
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
    public static PatchError StraddlesAWord(Address entry)
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
    public static PatchError UnrecognisedPrologue(Address entry)
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
    public static PatchError PositionDependentEntry(Address entry)
    {
        return new($"The instructions at {entry} depend on their address, so they can't be moved into a stub.");
    }

    /// <summary>
    ///     Creates the error for an entry that isn't redirected.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError NotRedirected()
    {
        return new("The entry isn't redirected.");
    }

    /// <summary>
    ///     Creates the error for an entry redirected by something other than this library.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError NotOurs()
    {
        return new("The entry is redirected by something other than this library, so it's left alone.");
    }

    /// <summary>
    ///     Creates the error for a patch written by another version of this library.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError AnotherVersion()
    {
        return new("The entry has a patch from another version of this library, which only that version can remove.");
    }

    /// <summary>
    ///     Creates the error for a page that claims a different entry than the one it was reached from.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError ClaimsAnotherEntry()
    {
        return new("The page claims a different entry, so nothing is assumed about it.");
    }

    /// <summary>
    ///     Creates the error for a page whose displaced length isn't one this library writes.
    /// </summary>
    /// <param name="stolen">The length the page records.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError ImplausibleStolenLength(int stolen)
    {
        return new($"The page records {stolen} displaced bytes, which isn't a length this library writes, so it's left alone.");
    }

    /// <summary>
    ///     Creates the error for a call read on a patch that only counts.
    /// </summary>
    /// <param name="entry">The patched entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError CapturesNothing(Address entry)
    {
        return new($"The patch at {entry} captures nothing, so there's no call to read.");
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
    public static PatchError StubTooLarge(Address entry, int wants, int room)
    {
        return new($"The stub for {entry} is {wants} bytes, and a slot has room for {room}. Capture less.");
    }

    /// <summary>
    ///     Creates the error for a capture width that no single store covers.
    /// </summary>
    /// <param name="width">The width that was asked for.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError UnstorableWidth(int width)
    {
        return new($"A capture of {width} bytes isn't a width 1 store covers. A store covers 1, 2, 4 or 8 bytes.");
    }

    /// <summary>
    ///     Creates the error for a capture wider than a store on the target moves.
    /// </summary>
    /// <param name="width">The width that was asked for.</param>
    /// <param name="pointerWidth">The width of a store on the target.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError WidthBeyondPointer(int width, int pointerWidth)
    {
        return new($"A capture of {width} bytes is wider than the {pointerWidth} bytes a store on this target moves.");
    }

    /// <summary>
    ///     Creates the error for a capture that would write outside the record's payload.
    /// </summary>
    /// <param name="width">The width of the capture.</param>
    /// <param name="recordOffset">The offset the capture writes at.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError OutsidePayload(int width, int recordOffset)
    {
        return new($"A capture of {width} bytes at 0x{recordOffset:X} falls outside the record's payload.");
    }

    /// <summary>
    ///     Creates the error for 2 captures that write over each other.
    /// </summary>
    /// <param name="earlier">The offset of the first capture.</param>
    /// <param name="later">The offset of the capture that overlaps it.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError OverlappingCaptures(int earlier, int later)
    {
        return new($"The captures at 0x{earlier:X} and 0x{later:X} overlap, so 1 would be read as the other.");
    }

    /// <summary>
    ///     Creates the error for a capture source a stub can't read from.
    /// </summary>
    /// <param name="source">The source that was asked for.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError UnknownCaptureSource(CaptureSource source)
    {
        return new($"A stub can't capture from {source}.");
    }

    /// <summary>
    ///     Creates the error for a register capture on a 32-bit target.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError NoRegisters()
    {
        return new("A 32-bit target passes every argument on the stack, so there's no register to capture.");
    }

    /// <summary>
    ///     Creates the error for a register capture past the 4 registers the x64 convention passes arguments in.
    /// </summary>
    /// <param name="index">The argument position that was asked for.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError NotInARegister(int index)
    {
        return new($"Argument {index} isn't in a register. Only the first 4 are.");
    }

    /// <summary>
    ///     Creates the error for a floating point capture with a width no floating point value has.
    /// </summary>
    /// <param name="width">The width that was asked for.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError FloatWidth(int width)
    {
        return new($"A floating point value is 4 or 8 bytes wide, not {width}.");
    }

    /// <summary>
    ///     Creates the error for a floating point capture that asks to be read through.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError FloatIsNotAnAddress()
    {
        return new("A floating point value can't be read through, because it isn't an address.");
    }

    /// <summary>
    ///     Creates the error for a stack capture before the first slot an argument can be in.
    /// </summary>
    /// <param name="index">The slot that was asked for.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PatchError NotAStackSlot(int index)
    {
        return new($"Stack slot {index} can't hold an argument. Slots count from 0, the first one above the return address.");
    }
}
