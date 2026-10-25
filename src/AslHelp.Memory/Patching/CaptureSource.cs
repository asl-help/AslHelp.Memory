namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents where a captured value sits when a patched entry is reached.
/// </summary>
public enum CaptureSource
{
    /// <summary>
    ///     In the register the calling convention gives that argument position, which exists on x64 only, because the
    ///     32-bit convention passes everything on the stack.
    /// </summary>
    Register,

    /// <summary>
    ///     In the floating point register that argument position is given, which is a different file of registers
    ///     counted off the same positions as the integer one.
    /// </summary>
    /// <remarks>
    ///     A float uses up the position a later argument would otherwise arrive in, so the 2 files are never
    ///     numbered separately. Reading the integer register at a float's position hands back whatever the caller last
    ///     left there, which reads as a number the method could plausibly have been called with.
    /// </remarks>
    FloatRegister,

    /// <summary>
    ///     On the caller's stack, at the slot that argument position is given.
    /// </summary>
    Stack,
}
