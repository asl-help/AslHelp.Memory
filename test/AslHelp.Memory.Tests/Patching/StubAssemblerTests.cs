using AslHelp.Memory.Patching;
using Iced.Intel;
using NUnit.Framework;
using System;
using CodeWriter = AslHelp.Memory.Patching.CodeWriter;

namespace AslHelp.Memory.Tests.Patching;

[TestFixture]
public class StubAssemblerTests
{
    private static readonly Address _code = new(0x140010000);
    private static readonly Address _record = new(0x140008000);

    private const ulong Code32 = 0x410000;
    private const ulong Record32 = 0x408000;

    // EmitIncrement on x64 emits a locked rip relative increment that reaches the record.
    [Test]
    public void EmitIncrement_X64_LocksAnIncrementThatReachesTheCounter()
    {
        CodeWriter code = new(_code);

        StubAssembler.X64.EmitIncrement(code, _record);

        var instruction = Decode(code, 64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(instruction.Mnemonic, Is.EqualTo(Mnemonic.Inc));
            Assert.That(instruction.HasLockPrefix, Is.True);
            Assert.That(instruction.MemorySize, Is.EqualTo(MemorySize.UInt64));
            Assert.That(instruction.IPRelativeMemoryAddress, Is.EqualTo(_record.Value));
            Assert.That(instruction.Length, Is.EqualTo(code.Length));
        }
    }

    // EmitIncrement on x86 addresses the record outright.
    [Test]
    public void EmitIncrement_X86_LocksAnIncrementAtTheCountersAddress()
    {
        CodeWriter code = new(new Address(Code32));

        StubAssembler.X86.EmitIncrement(code, new Address(Record32));

        var instruction = Decode(code, 32);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(instruction.Mnemonic, Is.EqualTo(Mnemonic.Inc));
            Assert.That(instruction.HasLockPrefix, Is.True);
            Assert.That(instruction.MemorySize, Is.EqualTo(MemorySize.UInt32));
            Assert.That(instruction.MemoryBase, Is.EqualTo(Register.None));
            Assert.That(instruction.MemoryDisplacement64, Is.EqualTo(Record32));
        }
    }

    // EmitClaim sets the sequence word's low bit. The jump BeginSkip emits goes to the instruction after EndSkip.
    [TestCase(true)]
    [TestCase(false)]
    public void EmitClaim_ThenBeginSkip_JumpsOnCarryPastTheCapture(bool is64Bit)
    {
        var assembler = StubAssembler.Create(is64Bit);
        CodeWriter code = new(is64Bit ? _code : new Address(Code32));

        assembler.EmitClaim(code, is64Bit ? _record : new Address(Record32));
        int skip = assembler.BeginSkip(code);
        code.Emit(0x90, 0x90, 0x90);
        assembler.EndSkip(code, skip);

        var decoder = Decoder.Create(is64Bit ? 64 : 32, new ByteArrayCodeReader(code.WrittenSpan.ToArray()));
        decoder.IP = CodeStart(code);
        decoder.Decode(out var claim);
        decoder.Decode(out var jump);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Mnemonic, Is.EqualTo(Mnemonic.Bts));
            Assert.That(claim.HasLockPrefix, Is.True);
            Assert.That(claim.Immediate8, Is.Zero);
            Assert.That(jump.Mnemonic, Is.EqualTo(Mnemonic.Jb));
            Assert.That(jump.NearBranchTarget, Is.EqualTo(code.NextAddress.Value));
        }
    }

    // EmitResume copies the displaced bytes and jumps back to the instruction after them.
    [TestCase(true)]
    [TestCase(false)]
    public void EmitResume_EmitsTheOriginalBytesThenJumpsBack(bool is64Bit)
    {
        var assembler = StubAssembler.Create(is64Bit);
        CodeWriter code = new(is64Bit ? _code : new Address(Code32));
        byte[] original = [0x48, 0x83, 0xEC, 0x28, 0x90];
        Address resumeAddress = new(is64Bit ? 0x140001005UL : 0x400005UL);

        assembler.EmitResume(code, original, resumeAddress);

        var decoder = Decoder.Create(is64Bit ? 64 : 32, new ByteArrayCodeReader(code.WrittenSpan[original.Length..].ToArray()));
        decoder.IP = CodeStart(code) + (ulong)original.Length;
        decoder.Decode(out var jump);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code.WrittenSpan[..original.Length].ToArray(), Is.EqualTo(original));
            Assert.That(jump.Mnemonic, Is.EqualTo(Mnemonic.Jmp));
            Assert.That(jump.NearBranchTarget, Is.EqualTo(resumeAddress.Value));
        }
    }

    // A register capture stores the register straight into the record, at the declared width.
    [TestCase(0, 8, Register.RCX)]
    [TestCase(1, 4, Register.EDX)]
    [TestCase(2, 2, Register.R8W)]
    [TestCase(3, 1, Register.R9L)]
    public void EmitRegisterCapture_X64_StoresTheArgumentRegisterAtItsWidth(int index, int width, Register register)
    {
        CodeWriter code = new(_code);

        var result = StubAssembler.X64.EmitRegisterCapture(code, _record + 0x18, index, width, indirect: false);
        var instruction = Decode(code, 64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(instruction.Mnemonic, Is.EqualTo(Mnemonic.Mov));
            Assert.That(instruction.Op1Register, Is.EqualTo(register));
            Assert.That(instruction.IPRelativeMemoryAddress, Is.EqualTo((_record + 0x18).Value));
        }
    }

    // A stack capture reads the slot past the pushed accumulator and the return address.
    [TestCase(true, 0, 16)]
    [TestCase(true, 2, 32)]
    [TestCase(false, 0, 8)]
    [TestCase(false, 3, 20)]
    public void EmitStackCapture_ReadsTheSlotAboveTheReturnAddress(bool is64Bit, int index, int displacement)
    {
        var assembler = StubAssembler.Create(is64Bit);
        CodeWriter code = new(is64Bit ? _code : new Address(Code32));

        var result = assembler.EmitStackCapture(code, (is64Bit ? _record : new Address(Record32)) + 0x18, index, is64Bit ? 8 : 4, indirect: false);

        var decoder = Decoder.Create(is64Bit ? 64 : 32, new ByteArrayCodeReader(code.WrittenSpan.ToArray()));
        decoder.IP = CodeStart(code);
        decoder.Decode(out var push);
        decoder.Decode(out var load);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(push.Mnemonic, Is.EqualTo(Mnemonic.Push));
            Assert.That(load.Mnemonic, Is.EqualTo(Mnemonic.Mov));
            Assert.That(load.MemoryBase, Is.EqualTo(is64Bit ? Register.RSP : Register.ESP));
            Assert.That(load.MemoryDisplacement64, Is.EqualTo((ulong)displacement));
        }
    }

    // A float capture stores the xmm register without touching the accumulator.
    [TestCase(0, 4, Mnemonic.Movss)]
    [TestCase(3, 8, Mnemonic.Movsd)]
    public void EmitFloatRegisterCapture_X64_StoresTheXmmRegister(int index, int width, Mnemonic mnemonic)
    {
        CodeWriter code = new(_code);

        var result = StubAssembler.X64.EmitFloatRegisterCapture(code, _record + 0x18, index, width, indirect: false);
        var instruction = Decode(code, 64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(instruction.Mnemonic, Is.EqualTo(mnemonic));
            Assert.That(instruction.Op1Register, Is.EqualTo(Register.XMM0 + index));
            Assert.That(instruction.IPRelativeMemoryAddress, Is.EqualTo((_record + 0x18).Value));
        }
    }

    // A capture the target can't do fails before any byte is emitted.
    [Test]
    public void Capture_AnImpossibleOne_FailsWithoutEmitting()
    {
        CodeWriter x64 = new(_code);
        CodeWriter x86 = new(new Address(Code32));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(StubAssembler.X86.EmitRegisterCapture(x86, new Address(Record32), 0, 4, false).IsErr, Is.True);
            Assert.That(StubAssembler.X86.EmitFloatRegisterCapture(x86, new Address(Record32), 0, 4, false).IsErr, Is.True);
            Assert.That(StubAssembler.X64.EmitRegisterCapture(x64, _record, 4, 8, false).IsErr, Is.True);
            Assert.That(StubAssembler.X64.EmitFloatRegisterCapture(x64, _record, 4, 8, false).IsErr, Is.True);
            Assert.That(StubAssembler.X64.EmitFloatRegisterCapture(x64, _record, 0, 2, false).IsErr, Is.True);
            Assert.That(StubAssembler.X64.EmitFloatRegisterCapture(x64, _record, 0, 8, true).IsErr, Is.True);
            Assert.That(StubAssembler.X64.EmitStackCapture(x64, _record, -1, 8, false).IsErr, Is.True);
            Assert.That(x64.Length, Is.Zero);
            Assert.That(x86.Length, Is.Zero);
        }
    }

    // A displacement counts from the end of the instruction, trailing bytes included.
    [Test]
    public void EmitRelative_WithTrailingBytes_CountsFromTheInstructionsEnd()
    {
        CodeWriter code = new(_code);

        code.Emit(0xE9);
        code.EmitRelative(_code + 0x100);
        code.Emit(0xF0, 0x48, 0x0F, 0xBA, 0x2D);
        code.EmitRelative(_record, trailing: 1);
        code.Emit(0x00);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(BitConverter.ToInt32(code.WrittenSpan.ToArray(), 1), Is.EqualTo(0x100 - 5));
            Assert.That(BitConverter.ToInt32(code.WrittenSpan.ToArray(), 10), Is.EqualTo((int)(_record - (_code + 15))));
            Assert.That(code.NextAddress, Is.EqualTo(_code + 15));
        }
    }

    private static Instruction Decode(CodeWriter code, int bitness)
    {
        var decoder = Decoder.Create(bitness, new ByteArrayCodeReader(code.WrittenSpan.ToArray()));
        decoder.IP = CodeStart(code);
        decoder.Decode(out var instruction);

        Assert.That(instruction.IsInvalid, Is.False, instruction.ToString());

        return instruction;
    }

    private static ulong CodeStart(CodeWriter code)
    {
        return code.NextAddress.Value - (ulong)code.Length;
    }
}
