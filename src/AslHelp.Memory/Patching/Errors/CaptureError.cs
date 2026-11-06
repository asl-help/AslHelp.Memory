using System.Globalization;

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
        return new(SR.FormatCaptureUnstorableWidth(width));
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
        return new(SR.FormatCaptureWidthBeyondPointer(width, pointerWidth));
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
        return new(SR.FormatCaptureOutsidePayload(width, recordOffset.ToString("X", CultureInfo.InvariantCulture)));
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
        return new(SR.FormatCaptureOverlappingCaptures(
            earlier.ToString("X", CultureInfo.InvariantCulture),
            later.ToString("X", CultureInfo.InvariantCulture)));
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
        return new(SR.FormatCaptureUnknownCaptureSource(source));
    }

    /// <summary>
    ///     Creates the error for a register capture on a 32-bit target.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CaptureError NoRegisters()
    {
        return new(SR.CaptureNoRegisters);
    }

    /// <summary>
    ///     Creates the error for a register capture past the 4 registers the x64 convention passes arguments in.
    /// </summary>
    /// <param name="index">The argument position that was asked for.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CaptureError RegisterIndexOutOfRange(int index)
    {
        return new(SR.FormatCaptureRegisterIndexOutOfRange(index));
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
        return new(SR.FormatCaptureFloatWidth(width));
    }

    /// <summary>
    ///     Creates the error for a floating point capture that asks to be read through.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CaptureError FloatNotAnAddress()
    {
        return new(SR.CaptureFloatNotAnAddress);
    }

    /// <summary>
    ///     Creates the error for a stack capture before the first slot an argument can be in.
    /// </summary>
    /// <param name="index">The slot that was asked for.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static CaptureError StackIndexOutOfRange(int index)
    {
        return new(SR.FormatCaptureStackIndexOutOfRange(index));
    }
}
