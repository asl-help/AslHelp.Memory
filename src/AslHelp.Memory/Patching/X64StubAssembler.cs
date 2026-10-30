using AslHelp.Memory.Patching.Errors;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Encodes stubs for a 64-bit Intel target, addressing the record relative to the instruction after each reference.
/// </summary>
internal sealed class X64StubAssembler : IntelStubAssembler
{
    /// <inheritdoc/>
    public override int PointerSize => 8;

    /// <inheritdoc/>
    /// <remarks>
    ///     <c>lock bts</c> sets the low bit and leaves its old value in the carry flag. A set bit means another thread
    ///     is halfway through its stores. The carry sends the current thread past them.
    /// </remarks>
    public override void EmitClaim(CodeWriter code, Address sequence)
    {
        // lock bts qword [rip+disp32], 0
        code.Emit(0xF0, 0x48, 0x0F, 0xBA, 0x2D);
        code.EmitRelative(sequence, trailing: 1);
        code.Emit(0x00);
    }

    /// <inheritdoc/>
    public override void EmitIncrement(CodeWriter code, Address counter)
    {
        // lock inc qword [rip+disp32]
        code.Emit(0xF0, 0x48, 0xFF, 0x05);
        code.EmitRelative(counter);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The convention passes the first 4 arguments in <c>rcx</c>, <c>rdx</c>, <c>r8</c> and <c>r9</c>. A
    ///     value goes straight from its register to the record. An address is followed through the accumulator.
    /// </remarks>
    public override Result EmitRegisterCapture(CodeWriter code, Address target, int index, int width, bool indirect)
    {
        int register = index switch
        {
            0 => 1,
            1 => 2,
            2 => 8,
            3 => 9,
            _ => -1,
        };

        if (register < 0)
        {
            return CaptureError.RegisterIndexOutOfRange(index);
        }

        if (!indirect)
        {
            EmitOperandSizeIfShort(code, width);

            int rex = (width == 8 ? 0x08 : 0x00) | (register >= 8 ? 0x04 : 0x00);

            if (rex != 0)
            {
                code.Emit((byte)(0x40 | rex));
            }

            // mov [rip+disp32], reg
            code.Emit((byte)(width == 1 ? 0x88 : 0x89), (byte)(0x05 | ((register & 7) << 3)));
            code.EmitRelative(target);

            return Result.Ok();
        }

        EmitPushScratch(code);

        // xor eax, eax
        code.Emit(0x31, 0xC0);

        // test reg, reg
        code.Emit((byte)(0x48 | (register >= 8 ? 0x05 : 0x00)), 0x85, (byte)(0xC0 | ((register & 7) << 3) | (register & 7)));

        // jz past the load, leaving the zero standing where the value would have gone
        int skip = code.Length;
        code.Emit(0x74, 0x00);

        // mov rax, [reg]
        code.Emit((byte)(0x48 | (register >= 8 ? 0x01 : 0x00)), 0x8B, (byte)(register & 7));

        EndSkip(code, skip);

        EmitStore(code, target, width);

        EmitPopScratch(code);

        return Result.Ok();
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Copies a float straight from its register to the record, without passing through the accumulator.
    /// </remarks>
    public override Result EmitFloatRegisterCapture(CodeWriter code, Address target, int index, int width, bool indirect)
    {
        if ((uint)index >= 4)
        {
            return CaptureError.RegisterIndexOutOfRange(index);
        }

        if (width is not (4 or 8))
        {
            return CaptureError.FloatWidth(width);
        }

        if (indirect)
        {
            return CaptureError.FloatNotAnAddress();
        }

        // movss or movsd [rip+disp32], xmm<index>
        code.Emit((byte)(width == 4 ? 0xF3 : 0xF2), 0x0F, 0x11, (byte)(0x05 | (index << 3)));
        code.EmitRelative(target);

        return Result.Ok();
    }

    protected override void EmitStore(CodeWriter code, Address target, int width)
    {
        EmitOperandSizeIfShort(code, width);

        // mov [rip+disp32], rax
        if (width == 8)
        {
            code.Emit(0x48);
        }

        code.Emit((byte)(width == 1 ? 0x88 : 0x89), 0x05);
        code.EmitRelative(target);
    }

    protected override void EmitRexW(CodeWriter code)
    {
        code.Emit(0x48);
    }
}
