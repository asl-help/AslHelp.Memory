namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents 1 value a stub copies out of a call and where it goes.
/// </summary>
/// <param name="Source">Where the value is when the entry is reached.</param>
/// <param name="Index">The register, counted in argument order, or the stack slot past the return address.</param>
/// <param name="RecordOffset">Where in the record the value is written.</param>
/// <param name="Width">The number of bytes of the value to write: 1, 2, 4 or 8.</param>
/// <param name="IsIndirect">Whether to write what the value points at rather than the value itself.</param>
/// <remarks>
///     <para>
///         A stub copies the value rather than where it was, since the place is only there while the call is
///         happening. A by value struct argument is the case that decides it: its bytes live in the caller's frame and
///         are gone by the time a reader asks.
///     </para>
///     <para>
///         The width is what the value's own type declares, never how much room the place it arrived in has. A slot
///         has whatever the caller last left above a narrower argument, measured on both runtimes, so copying the
///         whole slot would put another call's remains in the record beside a value this call really did pass.
///     </para>
/// </remarks>
public readonly record struct CaptureItem(
    CaptureSource Source,
    int Index,
    int RecordOffset,
    int Width,
    bool IsIndirect = false);
