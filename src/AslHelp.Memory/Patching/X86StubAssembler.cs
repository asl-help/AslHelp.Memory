using AslHelp.Memory.Patching.Errors;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Encodes stubs for a 32-bit Intel target, addressing the record by its absolute address. Everything arrives on the stack,
///     so no register is ever captured.
/// </summary>
internal sealed class X86StubAssembler : IntelStubAssembler
{
    /// <inheritdoc/>
    public override int PointerSize => 4;

    /// <inheritdoc/>
    public override void EmitClaim(CodeWriter code, Address sequence)
    {
        // lock bts dword [disp32], 0
        code.Emit(0xF0, 0x0F, 0xBA, 0x2D);
        code.EmitInt32(unchecked((int)(uint)sequence.Value));
        code.Emit(0x00);
    }

    /// <inheritdoc/>
    public override void EmitIncrement(CodeWriter code, Address counter)
    {
        // lock inc dword [disp32]
        code.Emit(0xF0, 0xFF, 0x05);
        code.EmitInt32(unchecked((int)(uint)counter.Value));
    }

    /// <inheritdoc/>
    public override Result EmitRegisterCapture(CodeWriter code, Address target, int index, int width, bool indirect)
    {
        return CaptureError.NoRegisters();
    }

    /// <inheritdoc/>
    public override Result EmitFloatRegisterCapture(CodeWriter code, Address target, int index, int width, bool indirect)
    {
        return CaptureError.NoRegisters();
    }

    protected override void EmitStore(CodeWriter code, Address target, int width)
    {
        EmitOperandSizeIfShort(code, width);

        // mov [disp32], eax
        code.Emit((byte)(width == 1 ? 0xA2 : 0xA3));
        code.EmitInt32(unchecked((int)(uint)target.Value));
    }

    protected override void EmitRexW(CodeWriter code) { }
}
