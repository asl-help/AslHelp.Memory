using AslHelp.Memory.Patching.Errors;
using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Encodes what the 2 Intel widths share: the accumulator as scratch, the null guard around an indirect load, and
///     the short jumps. The derived assemblers encode what differs.
/// </summary>
internal abstract class IntelStubAssembler : IStubAssembler
{
    private const byte PushAccumulator = 0x50;
    private const byte PopAccumulator = 0x58;

    /// <inheritdoc/>
    public abstract int PointerSize { get; }

    /// <inheritdoc/>
    public abstract void EmitClaim(CodeWriter code, Address sequence);

    /// <inheritdoc/>
    public abstract void EmitIncrement(CodeWriter code, Address counter);

    /// <inheritdoc/>
    public abstract Result EmitRegisterCapture(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <inheritdoc/>
    public abstract Result EmitFloatRegisterCapture(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <inheritdoc/>
    /// <remarks>
    ///     Uses the accumulator as scratch, because nothing is in it at a method entry and a push there is measured
    ///     safe on both widths. One width for the pushed accumulator and one for the return address
    ///     sit between the stack pointer and the first argument.
    /// </remarks>
    public Result EmitStackCapture(CodeWriter code, Address target, int index, int width, bool indirect)
    {
        if (index < 0)
        {
            return CaptureError.StackIndexOutOfRange(index);
        }

        EmitPushScratch(code);

        // mov rax, [rsp+disp32]
        EmitRexW(code);
        code.Emit(0x8B, 0x84, 0x24);
        code.EmitInt32(PointerSize + PointerSize + (index * PointerSize));

        if (indirect)
        {
            EmitGuardedLoad(code);
        }

        EmitStore(code, target, width);

        EmitPopScratch(code);

        return Result.Ok();
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A relative jump back clobbers no register and costs 5 bytes where an absolute form costs 14. The
    ///     session already refused an arena out of relative reach.
    /// </remarks>
    public void EmitResume(CodeWriter code, ReadOnlySpan<byte> original, Address resumeAddress)
    {
        code.Emit(original);

        code.Emit(0xE9);
        code.EmitRelative(resumeAddress);
    }

    /// <inheritdoc/>
    public int BeginSkip(CodeWriter code)
    {
        // jc rel8, resolved once the count is in place
        return EmitShortJump(code, 0x72);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Inside a slot the skip is under 107 bytes, so a short jump always reaches.
    /// </remarks>
    public void EndSkip(CodeWriter code, int skip)
    {
        code.Overwrite(skip + 1, (byte)(code.Length - (skip + 2)));
    }

    // Copies the declared width out of the accumulator into the record and nothing more. The rest of the record was
    // cleared before the stub could reach it.
    protected abstract void EmitStore(CodeWriter code, Address target, int width);

    // Emits the REX.W prefix that makes an accumulator instruction 8 bytes wide. Emits nothing at 4-byte pointers.
    protected abstract void EmitRexW(CodeWriter code);

    // Follows the accumulator when it points somewhere. A `0` is ordinary, because an unassigned reference is one,
    // and following it would fault on the game's own thread, so the `0` stays standing where the value would have
    // gone.
    protected void EmitGuardedLoad(CodeWriter code)
    {
        // test rax, rax
        EmitRexW(code);
        code.Emit(0x85, 0xC0);

        // jz past the load
        int skip = EmitShortJump(code, 0x74);

        // mov rax, [rax]
        EmitRexW(code);
        code.Emit(0x8B, 0x00);

        EndSkip(code, skip);
    }

    protected static void EmitOperandSizeIfShort(CodeWriter code, int width)
    {
        if (width == 2)
        {
            code.Emit(0x66);
        }
    }

    protected static void EmitPushScratch(CodeWriter code)
    {
        code.Emit(PushAccumulator);
    }

    protected static void EmitPopScratch(CodeWriter code)
    {
        code.Emit(PopAccumulator);
    }

    private static int EmitShortJump(CodeWriter code, byte opcode)
    {
        int at = code.Length;
        code.Emit(opcode, 0x00);

        return at;
    }
}
