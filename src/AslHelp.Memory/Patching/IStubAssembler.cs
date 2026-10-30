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
    ///     where <see cref="BeginSkip"/> reads it.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="sequence">Where the sequence word sits in the target.</param>
    void EmitClaim(CodeWriter code, Address sequence);

    /// <summary>
    ///     Emits the jump a thread takes when <see cref="EmitClaim"/> found the bit already set, over everything up to
    ///     where <see cref="EndSkip"/> is called.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <returns>
    ///     A handle <see cref="EndSkip"/> takes.
    /// </returns>
    int BeginSkip(CodeWriter code);

    /// <summary>
    ///     Makes the skip from <see cref="BeginSkip"/> reach the next instruction emitted.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="skip">What <see cref="BeginSkip"/> returned.</param>
    void EndSkip(CodeWriter code, int skip);

    /// <summary>
    ///     Emits an atomic step of a counter in the record.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="counter">Where the counter sits in the target.</param>
    void EmitIncrement(CodeWriter code, Address counter);

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
    Result EmitRegisterCapture(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <summary>
    ///     Emits the copy of a floating point argument register into the record.
    /// </summary>
    /// <inheritdoc cref="EmitRegisterCapture"/>
    Result EmitFloatRegisterCapture(CodeWriter code, Address target, int index, int width, bool indirect);

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
    Result EmitStackCapture(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <summary>
    ///     Emits the displaced bytes as they were, then the jump that resumes the entry past them.
    /// </summary>
    /// <param name="code">The stub being written.</param>
    /// <param name="original">The bytes the redirect displaced.</param>
    /// <param name="resumeAddress">The first byte of the entry the redirect left alone, where the jump goes.</param>
    void EmitResume(CodeWriter code, ReadOnlySpan<byte> original, Address resumeAddress);
}
