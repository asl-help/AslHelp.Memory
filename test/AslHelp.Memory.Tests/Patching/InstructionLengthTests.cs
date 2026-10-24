using AslHelp.Memory.Patching;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AslHelp.Memory.Tests.Patching;

[TestFixture]
public class InstructionLengthTests
{
    private static readonly char[] _separator = [' '];

    // A Mono x64 prologue decodes into its instructions.
    [Test]
    public void Decode_MonoX64Prologue_ReportsEachLength()
    {
        // mono_assembly_foreach, mono-2.0-bdwgc.dll from the 6000.3.21f1 x64 player.
        int[] expected = [5, 5, 5, 1, 4];

        Assert.That(DecodeEach("48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 20", is64Bit: true), Is.EqualTo(expected));
    }

    // A gs relative load with a SIB and no base decodes as absolute.
    [Test]
    public void Decode_MonoX64Accessor_ReportsEachLength()
    {
        // mono_domain_get from the same module. The gs relative load is a SIB with no base. It looks like the rip
        // relative form and isn't one.
        int[] expected = [6, 9, 5, 4];

        Assert.That(DecodeEach("8B 0D DE F4 69 00 65 48 8B 04 25 58 00 00 00 BA 30 00 00 00 48 8B 04 C8", is64Bit: true), Is.EqualTo(expected));
    }

    // A prologue with a prefetch and rip relative loads decodes into its instructions.
    [Test]
    public void Decode_MonoX64PrologueWithPrefetch_ReportsEachLength()
    {
        // mono_profiler_install_allocation from the same module. Mono's collector reaches for a prefetch early,
        // alongside the rip relative loads.
        int[] expected = [7, 4, 3, 2, 3, 7, 7, 7, 1, 3];

        Assert.That(
            DecodeEach(
                "48 8B 05 E9 B9 55 00 48 89 48 40 48 85 C9 74 3A 48 8B 08 0F 0D 89 70 01 00 00 4C 8D 05 BF FF FF FF 48 8B 91 70 01 00 00 90 48 8B C2",
                is64Bit: true),
            Is.EqualTo(expected));
    }

    // An IL2CPP x64 prologue decodes into its instructions.
    [Test]
    public void Decode_Il2CppX64Prologue_ReportsEachLength()
    {
        // DllGetActivationFactory, GameAssembly.dll from the 6000.3.21f1 x64 player.
        int[] expected = [5, 5, 1, 1, 1, 2, 2, 2, 2, 5, 7, 8, 3, 3];

        Assert.That(
            DecodeEach(
                "48 89 5C 24 10 48 89 4C 24 08 55 56 57 41 54 41 55 41 56 41 57 48 8D 6C 24 A0 48 81 EC 60 01 00 00 "
                + "0F 29 B4 24 50 01 00 00 4C 8B F2 48 85 C9",
                is64Bit: true),
            Is.EqualTo(expected));
    }

    // An IL2CPP x64 body with rip relative operands decodes into its instructions.
    [Test]
    public void Decode_Il2CppX64BodyWithRipRelativeOperands_ReportsEachLength()
    {
        // il2cpp_allocation_granularity from the same module, past its thunk.
        int[] expected = [2, 4, 7, 3, 2, 7, 5];

        Assert.That(
            DecodeEach("40 53 48 83 EC 20 80 3D 77 80 83 00 00 48 8B D9 75 13 48 8D 0D 1F 93 7D 00 E8 42 DA 02 00", is64Bit: true),
            Is.EqualTo(expected));
    }

    // A Mono x86 prologue decodes into its instructions.
    [Test]
    public void Decode_MonoX86Prologue_ReportsEachLength()
    {
        // mono_assembly_foreach, mono-2.0-bdwgc.dll from the 6000.3.21f1 x86 player.
        int[] expected = [1, 2, 7, 5, 2, 6];

        Assert.That(DecodeEach("55 8B EC 83 3D 5C 2A 57 10 00 68 44 2A 57 10 74 08 FF 15 D8 F1 40 10", is64Bit: false), Is.EqualTo(expected));
    }

    // A Mono x86 accessor decodes into its instructions.
    [Test]
    public void Decode_MonoX86Accessor_ReportsEachLength()
    {
        // mono_class_get_fields from the same module.
        int[] expected = [1, 2, 1, 3, 2, 2, 2, 1, 1, 1];

        Assert.That(DecodeEach("55 8B EC 57 8B 7D 0C 85 FF 75 05 33 C0 5F 5D C3", is64Bit: false), Is.EqualTo(expected));
    }

    // An MSVC x86 prologue decodes into its instructions.
    [Test]
    public void Decode_MsvcX86Prologue_ReportsEachLength()
    {
        // _BrotliDecoderDecompressStream@24, GameAssembly.dll from the 2021.3.11f1 x86 player.
        int[] expected = [1, 2, 3, 1, 3, 5, 1, 3, 4];

        Assert.That(DecodeEach("55 8B EC 83 EC 1C 53 8B 5D 08 BA 01 00 00 00 56 89 55 FC 83 7B 5C 00", is64Bit: false), Is.EqualTo(expected));
    }

    // An MSVC x86 prologue with fs relative operands decodes into its instructions.
    [Test]
    public void Decode_MsvcX86PrologueWithAbsoluteSegmentOperands_ReportsEachLength()
    {
        // DllGetActivationFactory from the same module. It sets up the exception frame.
        int[] expected = [1, 2, 6, 2, 5, 1, 7];

        Assert.That(DecodeEach("55 8B EC 64 A1 00 00 00 00 6A FF 68 10 EA 10 10 50 64 89 25 00 00 00 00", is64Bit: false), Is.EqualTo(expected));
    }

    // A rip relative operand makes the instruction position dependent.
    [TestCase("48 8D 0D 12 34 00 00", 7)] // lea rcx, [rip+0x3412]
    [TestCase("48 8B 05 34 12 00 00", 7)] // mov rax, [rip+0x1234]
    [TestCase("89 05 34 12 00 00", 6)] // mov [rip+0x1234], eax
    [TestCase("38 05 AC 8C 7F 00", 6)] // cmp [rip+0x7F8CAC], al
    [TestCase("80 3D 77 80 83 00 00", 7)] // cmp byte ptr [rip+0x838077], 0
    [TestCase("C6 05 00 00 00 00 01", 7)] // mov byte ptr [rip+0], 1
    [TestCase("F2 0F 10 05 00 00 00 00", 8)] // movsd xmm0, [rip+0]
    [TestCase("FF 15 12 34 00 00", 6)] // call qword ptr [rip+0x3412]
    [TestCase("67 48 8D 05 00 00 00 00", 8)] // lea rax, [eip+0]
    public void Decode_ARipRelativeOperandIn64BitCode_ReportsItRelative(string hex, int expected)
    {
        var (length, isRelative) = Decode(hex, is64Bit: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(length, Is.EqualTo(expected));
            Assert.That(isRelative, Is.True);
        }
    }

    // The rip relative ModRM form is relative only in 64-bit code.
    [Test]
    public void Decode_ModZeroRmFive_IsRelativeOnlyIn64BitCode()
    {
        // The same bytes are a rip relative load in 64 bit code and an absolute one in 32 bit code.
        var wide = Decode("8B 05 78 56 34 12", is64Bit: true);
        var narrow = Decode("8B 05 78 56 34 12", is64Bit: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wide, Is.EqualTo((6, true)));
            Assert.That(narrow, Is.EqualTo((6, false)));
        }
    }

    // A SIB, register or absolute operand in 64-bit code is not position dependent.
    [TestCase("48 8B 04 25 30 00 00 00", 8)] // mov rax, [0x30], a SIB with no base
    [TestCase("65 48 8B 04 25 58 00 00 00", 9)] // mov rax, gs:[0x58]
    [TestCase("48 89 5C 24 08", 5)] // mov [rsp+8], rbx
    [TestCase("48 8B 04 C8", 4)] // mov rax, [rax+rcx*8]
    [TestCase("0F 29 B4 24 50 01 00 00", 8)] // movaps [rsp+0x150], xmm6
    public void Decode_AnAbsoluteOrRegisterOperandIn64BitCode_ReportsItAbsolute(string hex, int expected)
    {
        Assert.That(Decode(hex, is64Bit: true), Is.EqualTo((expected, false)));
    }

    // An absolute operand in 32-bit code is not position dependent.
    [TestCase("FF 15 D8 F1 40 10", 6)] // call dword ptr [0x1040F1D8]
    [TestCase("83 3D 5C 2A 57 10 00", 7)] // cmp dword ptr [0x10572A5C], 0
    [TestCase("64 89 25 00 00 00 00", 7)] // mov fs:[0], esp
    [TestCase("FF 24 85 9C 93 09 10", 7)] // jmp dword ptr [eax*4+0x1009939C]
    public void Decode_AnAbsoluteOperandIn32BitCode_ReportsItAbsolute(string hex, int expected)
    {
        Assert.That(Decode(hex, is64Bit: false), Is.EqualTo((expected, false)));
    }

    // A relative branch is position dependent.
    [TestCase("E8 00 00 00 00", 5)] // call rel32
    [TestCase("E9 5B 00 00 00", 5)] // jmp rel32
    [TestCase("EB 0E", 2)] // jmp rel8
    [TestCase("74 08", 2)] // je rel8
    [TestCase("75 13", 2)] // jne rel8
    [TestCase("7F 10", 2)] // jg rel8
    [TestCase("0F 84 80 00 00 00", 6)] // je rel32
    [TestCase("0F 87 D2 00 00 00", 6)] // ja rel32
    [TestCase("E2 FE", 2)] // loop rel8
    public void Decode_Branches_ReportRelative(string hex, int expected)
    {
        var (length, isRelative) = Decode(hex, is64Bit: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(length, Is.EqualTo(expected));
            Assert.That(isRelative, Is.True);
        }
    }

    // Every recommended multi-byte nop decodes to its length.
    [TestCase("90", 1)]
    [TestCase("66 90", 2)]
    [TestCase("0F 1F 00", 3)]
    [TestCase("0F 1F 40 00", 4)]
    [TestCase("0F 1F 44 00 00", 5)]
    [TestCase("66 0F 1F 44 00 00", 6)]
    [TestCase("0F 1F 80 00 00 00 00", 7)]
    [TestCase("0F 1F 84 00 00 00 00 00", 8)]
    [TestCase("66 0F 1F 84 00 00 00 00 00", 9)]
    [TestCase("66 66 0F 1F 84 00 00 00 00 00", 10)]
    public void Decode_AMultiByteNop_ReportsItsLength(string hex, int expected)
    {
        Assert.That(Decode(hex, is64Bit: true), Is.EqualTo((expected, false)));
    }

    // Common x64 encodings decode to their length.
    [TestCase("48 B8 88 77 66 55 44 33 22 11", 10)] // movabs rax, imm64
    [TestCase("B8 01 00 00 01", 5)] // mov eax, imm32
    [TestCase("66 B8 34 12", 4)] // mov ax, imm16
    [TestCase("48 C7 C0 01 00 00 00", 7)] // mov rax, 1, where REX.W keeps the 4-byte immediate
    [TestCase("66 C7 45 FC 00 00", 6)] // mov word ptr [rbp-4], 0
    [TestCase("48 81 EC 60 01 00 00", 7)] // sub rsp, 0x160
    [TestCase("66 81 EC 10 00", 5)] // sub sp, 0x10
    [TestCase("48 83 EC 20", 4)] // sub rsp, 0x20
    [TestCase("F0 FF 00", 3)] // lock inc dword ptr [rax]
    [TestCase("F3 0F 10 45 F0", 5)] // movss xmm0, [rbp-0x10]
    [TestCase("67 8B 00", 3)] // mov eax, [eax]
    [TestCase("48 A1 88 77 66 55 44 33 22 11", 10)] // movabs rax, [imm64]
    [TestCase("67 A1 78 56 34 12", 6)] // mov eax, [imm32]
    [TestCase("48 63 C1", 3)] // movsxd rax, ecx
    [TestCase("41 54", 2)] // push r12
    [TestCase("40 53", 2)] // push rbx
    [TestCase("48 0F 45 C1", 4)] // cmovne rax, rcx
    [TestCase("0F B6 45 08", 4)] // movzx eax, byte ptr [rbp+8]
    [TestCase("0F 95 C0", 3)] // setne al
    [TestCase("69 C0 34 12 00 00", 6)] // imul eax, eax, 0x1234
    [TestCase("6B C0 04", 3)] // imul eax, eax, 4
    [TestCase("68 78 56 34 12", 5)] // push imm32
    [TestCase("6A 00", 2)] // push imm8
    [TestCase("C2 08 00", 3)] // ret 8
    [TestCase("C3", 1)] // ret
    [TestCase("C9", 1)] // leave
    [TestCase("CC", 1)] // int3
    [TestCase("C8 10 00 00", 4)] // enter 0x10, 0
    [TestCase("0F C8", 2)] // bswap eax
    public void Decode_X64_Length(string hex, int expected)
    {
        Assert.That(Decode(hex, is64Bit: true), Is.EqualTo((expected, false)));
    }

    // Common x86 encodings decode to their length.
    [TestCase("A1 08 2A 57 10", 5)] // mov eax, [0x10572A08]
    [TestCase("64 A1 2C 00 00 00", 6)] // mov eax, fs:[0x2C]
    [TestCase("67 8B 07", 3)] // mov ax, [bx], sixteen bit addressing with no displacement
    [TestCase("67 8B 46 08", 4)] // mov ax, [bp+8]
    [TestCase("67 8B 06 34 12", 5)] // mov ax, [0x1234]
    [TestCase("40", 1)] // inc eax, which is a REX prefix in 64 bit code
    [TestCase("8B 80 18 00 00 00", 6)] // mov eax, [eax+0x18]
    [TestCase("0F 45 C8", 3)] // cmovne ecx, eax
    public void Decode_X86_Length(string hex, int expected)
    {
        Assert.That(Decode(hex, is64Bit: false), Is.EqualTo((expected, false)));
    }

    // A group opcode takes its immediate from the ModRM reg field.
    [TestCase("F6 C0 01", 3)] // test al, 1, where reg 0 has an immediate
    [TestCase("F6 D8", 2)] // neg al, where reg 3 does not
    [TestCase("F7 C1 01 00 00 00", 6)] // test ecx, 1
    [TestCase("66 F7 C1 01 00", 5)] // test cx, 1
    [TestCase("48 F7 D8", 3)] // neg rax
    [TestCase("F7 D0", 2)] // not eax
    [TestCase("48 F7 E1", 3)] // mul rcx
    [TestCase("48 F7 F1", 3)] // div rcx
    [TestCase("FF E0", 2)] // jmp rax
    [TestCase("FF D0", 2)] // call rax
    [TestCase("FF 75 08", 3)] // push qword ptr [rbp+8]
    [TestCase("FE C0", 2)] // inc al
    [TestCase("80 7C 24 08 00", 5)] // cmp byte ptr [rsp+8], 0
    [TestCase("83 C0 1F", 3)] // add eax, 0x1F
    [TestCase("0F BA E0 07", 4)] // bt eax, 7
    public void Decode_AnOpcodeGroupsMember_ReportsItsLength(string hex, int expected)
    {
        Assert.That(Decode(hex, is64Bit: true), Is.EqualTo((expected, false)));
    }

    // A buffer that ends inside an instruction is refused.
    [TestCase("")] // nothing to decode
    [TestCase("48")] // a REX prefix and no opcode
    [TestCase("0F")] // the escape byte and no opcode
    [TestCase("66 66 66")] // prefixes and no opcode
    [TestCase("48 8B")] // no ModRM
    [TestCase("48 89 5C 24")] // a SIB whose displacement is cut off
    [TestCase("48 8D 0D 12 34")] // a rip relative displacement cut off
    [TestCase("E8 00 00")] // a branch displacement cut off
    [TestCase("48 B8 00 00 00 00")] // an 8-byte immediate cut off
    [TestCase("F6 C0")] // a group immediate cut off
    public void Decode_TruncatedBuffer_Refuses(string hex)
    {
        Assert.That(Decodes(hex, is64Bit: true), Is.False);
    }

    // An encoding outside the supported maps is refused.
    [TestCase("06")] // push es, which is not an encoding in 64 bit code
    [TestCase("9A 00 00 00 00 00 00")] // the far call
    [TestCase("0F 38 00 C0")] // pshufb, out of the 3-byte map
    [TestCase("0F 3A 0F C1 04")] // palignr, out of the other 3-byte map
    [TestCase("C5 F8 28 C1")] // vmovaps, behind a 2-byte VEX prefix
    [TestCase("C4 E2 71 F7 C0")] // shlx, behind a 3-byte VEX prefix
    [TestCase("62 F1 7C 48 28 C1")] // vmovaps, behind an EVEX prefix
    [TestCase("66 E8 00 00 00 00")] // a near call whose displacement width vendors disagree on
    [TestCase("66 0F 84 00 00 00 00")] // the same disagreement on a near jcc
    [TestCase("48 40 8B C1")] // 2 REX prefixes
    [TestCase("C7 F8 00 00 00 00")] // xbegin, which shares its opcode with mov and branches
    [TestCase("C6 F8 00")] // xabort, sharing the byte sized opcode
    [TestCase("FF 1D 00 00 00 00")] // the far call out of group 5
    [TestCase("FF 38")] // the reg 7 hole in group 5
    [TestCase("0F FF 00")] // ud0
    [TestCase("0F B8 C0")] // popcnt, whose opcode also spells the obsolete jmpe
    public void Decode_UnsupportedEncoding_Refuses(string hex)
    {
        Assert.That(Decodes(hex, is64Bit: true), Is.False);
    }

    // An 8F byte with a nonzero ModRM reg field is an XOP escape and is refused in both modes.
    [TestCase("8F E8 78 C0 C1 05", true)] // vprotb xmm0, xmm1, 5
    [TestCase("8F E8 78 C0 C1 05", false)]
    [TestCase("8F E9 78 90 C1", true)] // vprotb xmm0, xmm1, xmm0
    [TestCase("8F 08 00 00", true)] // reg 1, which pop doesn't use
    public void Decode_XopEscape_Refuses(string hex, bool is64Bit)
    {
        Assert.That(Decodes(hex, is64Bit), Is.False);
    }

    // pop r/m is the reg 0 form of 8F.
    [TestCase("8F 00", 2)] // pop qword ptr [rax]
    [TestCase("8F 45 08", 3)] // pop qword ptr [rbp+8]
    public void Decode_PopToMemory_ReportsItsLength(string hex, int expected)
    {
        Assert.That(Decode(hex, is64Bit: true), Is.EqualTo((expected, false)));
    }

    // The movsxd byte is arpl in 32-bit code and is refused.
    [Test]
    public void Decode_Arpl_IsRefusedIn32BitCode()
    {
        // The byte that means movsxd in 64 bit code.
        Assert.That(Decodes("63 C1", is64Bit: false), Is.False);
    }

    // A refused instruction leaves both outputs at their defaults.
    [TestCase("0F 38 00 C0")] // refused on the opcode, before anything is decoded
    [TestCase("FF 1D 00 00 00 00")] // refused on the group, after a rip relative operand was seen
    [TestCase("C6 05 00 00 00 00")] // refused on the cut off immediate, likewise
    public void Decode_Refusal_LeavesTheOutputsClear(string hex)
    {
        Refuse(hex, out int length, out bool isRelative);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(length, Is.Zero);
            Assert.That(isRelative, Is.False);
        }
    }

    // A first instruction that covers the request is the whole run.
    [Test]
    public void Steal_AFirstInstructionAlreadyLongEnough_StopsOnIt()
    {
        // mono_assembly_foreach on x64 opens with a 5-byte store.
        Assert.That(Steal("48 89 5C 24 08 48 89 6C 24 10", is64Bit: true, atLeast: 5), Is.EqualTo((5, false)));
    }

    // Short instructions add up until they cover the request.
    [Test]
    public void Steal_InstructionsShorterThanTheRequest_AccumulatesUntilItReachesIt()
    {
        // push rbx then sub rsp, 0x20.
        Assert.That(Steal("40 53 48 83 EC 20 80 3D 77 80 83 00 00", is64Bit: true, atLeast: 5), Is.EqualTo((6, false)));
    }

    // A rip relative instruction in the run makes the run position dependent.
    [Test]
    public void Steal_APositionDependentInstructionAmongThoseStolen_ReportsTheRunRelative()
    {
        // Reaching 8 bytes pulls in the rip relative compare that follows the frame setup.
        Assert.That(Steal("40 53 48 83 EC 20 80 3D 77 80 83 00 00", is64Bit: true, atLeast: 8), Is.EqualTo((13, true)));
    }

    // A function that opens with a relative jump makes the run position dependent.
    [Test]
    public void Steal_AFunctionAlreadyJumpingElsewhere_ReportsTheRunRelative()
    {
        Assert.That(Steal("E9 5B 00 00 00", is64Bit: true, atLeast: 5), Is.EqualTo((5, true)));
    }

    // A rip relative first instruction makes the run position dependent.
    [Test]
    public void Steal_ARipRelativeFirstInstruction_ReportsTheRunRelative()
    {
        Assert.That(Steal("48 8D 0D 1F 93 7D 00", is64Bit: true, atLeast: 5), Is.EqualTo((7, true)));
    }

    // A run ends on an instruction boundary past the request.
    [Test]
    public void Steal_A32BitPrologue_TakesWholeInstructionsPastTheRequest()
    {
        Assert.That(Steal("55 8B EC 83 EC 1C 53", is64Bit: false, atLeast: 5), Is.EqualTo((6, false)));
    }

    // An absolute address in a 32-bit run keeps the run movable.
    [Test]
    public void Steal_A32BitPrologueReadingAnAbsoluteAddress_ReportsTheRunAbsolute()
    {
        // The absolute displacement is not position dependent, so the whole run stays movable.
        Assert.That(Steal("55 8B EC 83 3D 5C 2A 57 10 00", is64Bit: false, atLeast: 5), Is.EqualTo((10, false)));
    }

    // A run that can't be moved into a stub is refused.
    [TestCase("55 8B EC")] // the buffer runs out before the request is met
    [TestCase("55 06 8B EC 8B EC")] // an instruction it refuses stands in the way
    [TestCase("33 C0 C3 CC CC")] // a function shorter than the jump returns before it, so the next one would be overwritten
    [TestCase("FF 61 08 CC CC")] // a thunk jumping through memory never falls through
    [TestCase("FF D0 90 90 90")] // a call through a register would come back into the stub
    [TestCase("EB 05 90 90 90")] // a short jump never falls through, so the nops after it aren't on the path
    [TestCase("E8 00 00 00 00")] // a direct call would come back into the stub too
    public void Steal_Failing_Refuses(string hex)
    {
        Assert.That(Steals(hex, is64Bit: false, atLeast: 5), Is.False);
    }

    // A request for 0 bytes is refused.
    [Test]
    public void Steal_NothingAtAll_Refuses()
    {
        Assert.That(Steals("55 8B EC", is64Bit: false, atLeast: 0), Is.False);
    }

    // A refused run leaves both outputs at their defaults.
    [Test]
    public void Steal_Refusal_LeavesTheOutputsClear()
    {
        // The rip relative lea decodes, then the buffer runs out short of the request.
        _ = InstructionLength.TryStealLength(Bytes("48 8D 0D 1F 93 7D 00"), is64Bit: true, atLeast: 12, out int length, out bool anyRelative);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(length, Is.Zero);
            Assert.That(anyRelative, Is.False);
        }
    }

    private static bool Steals(string hex, bool is64Bit, int atLeast)
    {
        return InstructionLength.TryStealLength(Bytes(hex), is64Bit, atLeast, out _, out _);
    }

    private static (int Length, bool AnyRelative) Steal(string hex, bool is64Bit, int atLeast)
    {
        Assert.That(InstructionLength.TryStealLength(Bytes(hex), is64Bit, atLeast, out int length, out bool anyRelative), Is.True, hex);

        return (length, anyRelative);
    }

    private static bool Decodes(string hex, bool is64Bit)
    {
        return InstructionLength.TryDecode(Bytes(hex), is64Bit, out _, out _);
    }

    private static void Refuse(string hex, out int length, out bool isRelative)
    {
        Assert.That(InstructionLength.TryDecode(Bytes(hex), is64Bit: true, out length, out isRelative), Is.False, hex);
    }

    private static (int Length, bool IsRelative) Decode(string hex, bool is64Bit)
    {
        Assert.That(InstructionLength.TryDecode(Bytes(hex), is64Bit, out int length, out bool isRelative), Is.True, hex);

        return (length, isRelative);
    }

    // Returns the length of every instruction in the buffer, which must end on an instruction boundary.
    private static int[] DecodeEach(string hex, bool is64Bit)
    {
        byte[] code = Bytes(hex);
        List<int> lengths = [];

        int pos = 0;
        while (pos < code.Length)
        {
            Assert.That(InstructionLength.TryDecode(code.AsSpan(pos), is64Bit, out int length, out _), Is.True, $"refused at offset {pos}");

            lengths.Add(length);
            pos += length;
        }

        return [.. lengths];
    }

    private static byte[] Bytes(string hex)
    {
        string[] parts = hex.Split(_separator, StringSplitOptions.RemoveEmptyEntries);
        byte[] bytes = new byte[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return bytes;
    }
}
