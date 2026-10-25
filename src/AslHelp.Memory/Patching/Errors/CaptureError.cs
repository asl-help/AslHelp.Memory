namespace AslHelp.Memory.Patching.Errors;

/// <summary>
///     Represents a capture that a stub can't write.
/// </summary>
internal sealed record CaptureError : ResultError
{
    private CaptureError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a capture width that no single store covers.
    /// </summary>
    /// <param name="width">The width that was asked for.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CaptureError UnstorableWidth(int width)
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
    public static CaptureError WidthBeyondPointer(int width, int pointerWidth)
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
    public static CaptureError OutsidePayload(int width, int recordOffset)
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
    public static CaptureError OverlappingCaptures(int earlier, int later)
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
    public static CaptureError UnknownCaptureSource(CaptureSource source)
    {
        return new($"A stub can't capture from {source}.");
    }

    /// <summary>
    ///     Creates the error for a register capture on a 32-bit target.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CaptureError NoRegisters()
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
    public static CaptureError NotInARegister(int index)
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
    public static CaptureError FloatWidth(int width)
    {
        return new($"A floating point value is 4 or 8 bytes wide, not {width}.");
    }

    /// <summary>
    ///     Creates the error for a floating point capture that asks to be read through.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CaptureError FloatIsNotAnAddress()
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
    public static CaptureError NotAStackSlot(int index)
    {
        return new($"Stack slot {index} can't hold an argument. Slots count from 0, the first one above the return address.");
    }
}
