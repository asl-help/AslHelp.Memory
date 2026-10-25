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
    public abstract void Claim(CodeWriter code, Address sequence);

    /// <inheritdoc/>
    public abstract void Increment(CodeWriter code, Address counter);

    /// <inheritdoc/>
    public abstract Result CaptureRegister(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <inheritdoc/>
    public abstract Result CaptureFloatRegister(CodeWriter code, Address target, int index, int width, bool indirect);

    /// <inheritdoc/>
    /// <remarks>
    ///     Uses the accumulator as scratch, because nothing is in it at a method entry and a push there is measured
    ///     safe on both widths. One width for the pushed accumulator and one for the return address
    ///     sit between the stack pointer and the first argument.
    /// </remarks>
    public Result CaptureStack(CodeWriter code, Address target, int index, int width, bool indirect)
    {
        if (index < 0)
        {
            return CaptureError.NotAStackSlot(index);
        }

        PushScratch(code);

        // mov rax, [rsp+disp32]
        RexW(code);
        code.Emit(0x8B, 0x84, 0x24);
        code.EmitInt32(PointerSize + PointerSize + (index * PointerSize));

        if (indirect)
        {
            GuardedLoad(code);
        }

        Store(code, target, width);

        PopScratch(code);

        return Result.Ok();
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A relative jump back clobbers no register and costs 5 bytes where an absolute form costs 14. The
    ///     session already refused an arena out of relative reach.
    /// </remarks>
    public void Resume(CodeWriter code, ReadOnlySpan<byte> original, Address continueAt)
    {
        code.Emit(original);

        code.Emit(0xE9);
        code.EmitRelative(continueAt);
    }

    /// <inheritdoc/>
    public int SkipWhenClaimed(CodeWriter code)
    {
        // jc rel8, resolved once the count is in place
        return ShortJump(code, 0x72);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Inside a slot the skip is under 107 bytes, so a short jump always reaches.
    /// </remarks>
    public void ResolveSkip(CodeWriter code, int skip)
    {
        code.Patch(skip + 1, (byte)(code.Length - (skip + 2)));
    }

    // Copies the declared width out of the accumulator into the record and nothing more. The rest of the record was
    // cleared before the stub could reach it.
    protected abstract void Store(CodeWriter code, Address target, int width);

    // Emits the REX.W prefix that makes an accumulator instruction 8 bytes wide. Emits nothing at 4-byte pointers.
    protected abstract void RexW(CodeWriter code);

    // Follows the accumulator when it points somewhere. A `0` is ordinary, because an unassigned reference is one,
    // and following it would fault on the game's own thread, so the `0` stays standing where the value would have
    // gone.
    protected void GuardedLoad(CodeWriter code)
    {
        // test rax, rax
        RexW(code);
        code.Emit(0x85, 0xC0);

        // jz past the load
        int skip = ShortJump(code, 0x74);

        // mov rax, [rax]
        RexW(code);
        code.Emit(0x8B, 0x00);

        ResolveSkip(code, skip);
    }

    protected static void OperandSizeIfShort(CodeWriter code, int width)
    {
        if (width == 2)
        {
            code.Emit(0x66);
        }
    }

    protected static void PushScratch(CodeWriter code)
    {
        code.Emit(PushAccumulator);
    }

    protected static void PopScratch(CodeWriter code)
    {
        code.Emit(PopAccumulator);
    }

    private static int ShortJump(CodeWriter code, byte opcode)
    {
        int at = code.Length;
        code.Emit(opcode, 0x00);

        return at;
    }
}
