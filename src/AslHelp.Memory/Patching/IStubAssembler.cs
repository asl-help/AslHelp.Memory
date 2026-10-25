using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Encodes the instructions a stub is made of for one instruction set and width.
/// </summary>
/// <remarks>
///     A stub is the same short program on every target: claim the record, copy what the layout lists into it, step
///     the counters, run the displaced bytes and jump back. Only the encoding differs, so the patch and
///     the capture layout decide what the stub does and an assembler decides how, one per target the library can
///     write into.
/// </remarks>
internal interface IStubAssembler
{
    /// <summary>
    ///     Gets the width of a pointer on the target, in bytes.
    /// </summary>
    int PointerSize { get; }

    /// <summary>
    ///     Emits the claim on the record's sequence word: its low bit is set and whether it was already set is left
    ///     where <see cref="SkipWhenClaimed"/> reads it.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="sequence">Where the sequence word sits in the target.</param>
    void Claim(CodeWriter code, Address sequence);

    /// <summary>
    ///     Emits the jump a thread takes when <see cref="Claim"/> found the bit already set, over everything up to
    ///     where <see cref="ResolveSkip"/> is called.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <returns>
    ///     A handle <see cref="ResolveSkip"/> takes.
    /// </returns>
    int SkipWhenClaimed(CodeWriter code);

    /// <summary>
    ///     Makes the skip from <see cref="SkipWhenClaimed"/> reach the next instruction emitted.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="skip">What <see cref="SkipWhenClaimed"/> returned.</param>
    void ResolveSkip(CodeWriter code, int skip);

    /// <summary>
    ///     Emits an atomic step of a counter in the record.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="counter">Where the counter sits in the target.</param>
    void Increment(CodeWriter code, Address counter);

    /// <summary>
    ///     Emits the copy of an argument register into the record.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="target">Where in the target the bytes go.</param>
    /// <param name="index">Which argument, counting from the first the convention passes in a register.</param>
    /// <param name="width">How many bytes to copy.</param>
    /// <param name="indirect">Whether to copy from where the register points rather than the register itself.</param>
    /// <returns>
    ///     A successful result when the instruction exists on this target, otherwise a failed result with the reason.
    /// </returns>
    Result CaptureRegister(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <summary>
    ///     Emits the copy of a floating point argument register into the record.
    /// </summary>
    /// <inheritdoc cref="CaptureRegister"/>
    Result CaptureFloatRegister(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <summary>
    ///     Emits the copy of an argument the caller pushed into the record.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="target">Where in the target the bytes go.</param>
    /// <param name="index">Which stack slot, counting from the first above the return address.</param>
    /// <param name="width">How many bytes to copy.</param>
    /// <param name="indirect">Whether to copy from where the slot points rather than the slot itself.</param>
    /// <returns>
    ///     A successful result when the instruction exists on this target, otherwise a failed result with the reason.
    /// </returns>
    Result CaptureStack(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <summary>
    ///     Emits the way out: the displaced bytes as they were, then the jump back to where they came from.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="original">The bytes the redirect displaced.</param>
    /// <param name="continueAt">The first byte of the entry the redirect left alone.</param>
    void Resume(CodeWriter code, ReadOnlySpan<byte> original, Address continueAt);
}
