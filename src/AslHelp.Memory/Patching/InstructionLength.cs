using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Decodes how long the single x86 or x64 instruction at the start of a buffer is and whether that instruction
///     would mean something else at another address.
/// </summary>
/// <remarks>
///     The opcode maps are partial on purpose: the only code this looks at is the prologue of a compiled managed
///     function, either Mono's JIT output or what MSVC emits for IL2CPP. Every encoding whose length isn't certain is
///     refused, because a refusal only costs a patch, while a length 1 byte off leaves the game running from the middle
///     of an instruction.
/// </remarks>
internal static class InstructionLength
{
    /// <summary>
    ///     Decodes the length of the single instruction at the start of <paramref name="code"/>.
    /// </summary>
    /// <param name="code">The bytes to decode, starting on an instruction boundary.</param>
    /// <param name="is64Bit">Whether the code runs in 64-bit mode.</param>
    /// <param name="length">The instruction's length in bytes, or <c>0</c> when it was not decoded.</param>
    /// <param name="isRelative">
    ///     Whether the instruction reaches memory or a branch target relative to its own address.
    /// </param>
    /// <returns>
    ///     Whether the instruction was decoded.
    /// </returns>
    public static bool TryDecode(ReadOnlySpan<byte> code, bool is64Bit, out int length, out bool isRelative)
    {
        length = 0;
        isRelative = false;

        int pos = 0;
        bool operand16 = false;
        bool rexW = false;
        int addressSize = is64Bit ? 8 : 4;

        bool prefixes = true;
        while (prefixes && pos < code.Length)
        {
            switch (code[pos])
            {
                case 0x66:
                {
                    operand16 = true;
                    pos++;
                    break;
                }
                case 0x67:
                {
                    // Selects 16-bit addressing in 32-bit code. In 64-bit code `67` selects 32-bit addressing, which
                    // keeps the ModRM and SIB layout.
                    addressSize = is64Bit ? 4 : 2;
                    pos++;
                    break;
                }
                case 0xF0 or 0xF2 or 0xF3 or 0x26 or 0x2E or 0x36 or 0x3E or 0x64 or 0x65:
                {
                    pos++;
                    break;
                }
                default:
                {
                    prefixes = false;
                    break;
                }
            }
        }

        // Takes a REX prefix only right before the opcode. A second one falls through to the opcode maps and is
        // refused there.
        if (is64Bit && pos < code.Length && (code[pos] & 0xF0) == 0x40)
        {
            rexW = (code[pos] & 0x08) != 0;
            pos++;
        }

        if (pos >= code.Length)
        {
            return false;
        }

        byte opcode = code[pos++];
        Shape shape;

        if (opcode == 0x0F)
        {
            if (pos >= code.Length)
            {
                return false;
            }

            shape = TwoByteShape(code[pos++], operand16);
        }
        else
        {
            shape = OneByteShape(opcode, is64Bit, operand16);
        }

        if (shape == Shape.Refused)
        {
            return false;
        }

        int reg = 0;
        bool relative = false;

        if (HasModRm(shape))
        {
            if (!TryDecodeMemory(code, ref pos, is64Bit, addressSize, out reg, out bool ripRelative))
            {
                return false;
            }

            relative = ripRelative;
        }

        if (!TryImmediate(shape, reg, operand16, rexW, addressSize, out int immediate, out bool branches))
        {
            return false;
        }

        const int MaxLength = 15;

        pos += immediate;
        if (pos > code.Length || pos > MaxLength)
        {
            return false;
        }

        length = pos;
        isRelative = relative || branches;

        return true;
    }

    private static bool HasModRm(Shape shape)
    {
        return shape is Shape.ModRm
            or Shape.ModRmImm8
            or Shape.ModRmImmZ
            or Shape.Group3Byte
            or Shape.Group3Word
            or Shape.MovImm8
            or Shape.MovImmZ
            or Shape.Group5;
    }

    // Returns the ModRM reg field too, because the group opcodes keep part of their opcode in it.
    private static bool TryDecodeMemory(ReadOnlySpan<byte> code, ref int pos, bool is64Bit, int addressSize, out int reg, out bool ripRelative)
    {
        reg = 0;
        ripRelative = false;

        if (pos >= code.Length)
        {
            return false;
        }

        byte modrm = code[pos++];
        int mod = modrm >> 6, rm = modrm & 7;

        reg = (modrm >> 3) & 7;

        if (mod == 3)
        {
            return true;
        }

        int displacement;
        if (addressSize == 2)
        {
            displacement = (mod, rm) switch
            {
                (0, 6) => 2,
                (0, _) => 0,
                (1, _) => 1,
                _ => 2,
            };
        }
        else if (rm == 4)
        {
            if (pos >= code.Length)
            {
                return false;
            }

            int baseReg = code[pos++] & 7;

            displacement = (mod, baseReg) switch
            {
                (0, 5) => 4,
                (0, _) => 0,
                (1, _) => 1,
                _ => 4,
            };
        }
        else if (mod == 0 && rm == 5)
        {
            // Flags the one encoding whose meaning depends on its address. In 64-bit code it counts from the end of the
            // instruction. In 32-bit code it's a plain absolute address.
            displacement = 4;
            ripRelative = is64Bit;
        }
        else
        {
            displacement = mod switch
            {
                1 => 1,
                2 => 4,
                _ => 0,
            };
        }

        pos += displacement;

        return pos <= code.Length;
    }

    private static bool TryImmediate(Shape shape, int reg, bool operand16, bool rexW, int addressSize, out int size, out bool branches)
    {
        branches = false;

        // Narrows the immediate to 2 bytes on a `66` prefix. REX.W outranks it and keeps the sign extended 4-byte
        // form.
        int z = operand16 && !rexW ? 2 : 4;

        switch (shape)
        {
            case Shape.None:
            case Shape.ModRm:
            {
                size = 0;
                return true;
            }
            case Shape.Imm8:
            case Shape.ModRmImm8:
            {
                size = 1;
                return true;
            }
            case Shape.Imm16:
            {
                size = 2;
                return true;
            }
            case Shape.ImmZ:
            case Shape.ModRmImmZ:
            {
                size = z;
                return true;
            }
            case Shape.ImmV:
            {
                size = rexW ? 8 : z;
                return true;
            }
            case Shape.Enter:
            {
                size = 3;
                return true;
            }
            case Shape.MovOffset:
            {
                size = addressSize;
                return true;
            }
            case Shape.RelImm8:
            {
                branches = true;
                size = 1;
                return true;
            }
            case Shape.RelImmZ:
            {
                branches = true;
                size = z;
                return true;
            }
            case Shape.Group3Byte:
            {
                size = reg <= 1 ? 1 : 0;
                return true;
            }
            case Shape.Group3Word:
            {
                size = reg <= 1 ? z : 0;
                return true;
            }
            case Shape.MovImm8:
            case Shape.MovImmZ:
            {
                // Takes only the `/0` form, the mov. The `/7` forms are the transactional memory pair and one of
                // them has a branch displacement, so both are refused.
                size = shape == Shape.MovImm8 ? 1 : z;
                return reg == 0;
            }
            case Shape.Group5:
            {
                // Refuses the far call and far jump forms (`/3`, `/5`), because their operand isn't modeled here.
                size = 0;
                return reg is 0 or 1 or 2 or 4 or 6;
            }
            case Shape.Refused:
            default:
            {
                size = 0;
                return false;
            }
        }
    }

    private static Shape OneByteShape(byte opcode, bool is64Bit, bool operand16)
    {
        return opcode switch
        {
            // Covers the 8 arithmetic operations, told apart by the low 3 bits. The `6` and `7` forms are segment
            // pushes and decimal adjusts, which no compiler emits, so they fall to the refusal at the end.
            <= 0x3D when (opcode & 7) <= 5 => (opcode & 7) switch
            {
                <= 3 => Shape.ModRm,
                4 => Shape.Imm8,
                _ => Shape.ImmZ,
            },

            // Refused in 64-bit code, where this byte is only reachable as a second REX prefix.
            >= 0x40 and <= 0x4F => is64Bit ? Shape.Refused : Shape.None,

            >= 0x50 and <= 0x5F => Shape.None, // push and pop of a register

            // movsxd. In 32-bit code this byte is arpl, which no compiler emits.
            0x63 => is64Bit ? Shape.ModRm : Shape.Refused,

            0x68 => Shape.ImmZ, // push imm
            0x69 => Shape.ModRmImmZ, // imul r, r/m, imm
            0x6A => Shape.Imm8, // push imm8
            0x6B => Shape.ModRmImm8, // imul r, r/m, imm8

            >= 0x70 and <= 0x7F => Shape.RelImm8, // jcc rel8

            0x80 => Shape.ModRmImm8, // group 1 on a byte, with imm8
            0x81 => Shape.ModRmImmZ, // group 1 with a full width immediate
            0x83 => Shape.ModRmImm8, // group 1 with a sign extended imm8

            >= 0x84 and <= 0x8F => Shape.ModRm, // test, xchg, mov, lea, pop r/m

            >= 0x90 and <= 0x99 => Shape.None, // nop, xchg with the accumulator, the sign extensions
            >= 0x9C and <= 0x9F => Shape.None, // the flag register moves

            // mov between the accumulator and an absolute address. The address width follows the address size, not the
            // operand size.
            >= 0xA0 and <= 0xA3 => Shape.MovOffset,

            >= 0xA4 and <= 0xA7 => Shape.None, // movs, cmps
            0xA8 => Shape.Imm8, // test al, imm8
            0xA9 => Shape.ImmZ, // test eax, imm
            >= 0xAA and <= 0xAF => Shape.None, // stos, lods, scas

            >= 0xB0 and <= 0xB7 => Shape.Imm8, // mov r8, imm8
            >= 0xB8 and <= 0xBF => Shape.ImmV, // mov r, imm, the only immediate that reaches 8 bytes

            0xC0 or 0xC1 => Shape.ModRmImm8, // the shift group, by imm8
            0xC2 => Shape.Imm16, // ret imm16
            0xC3 => Shape.None, // ret
            0xC6 => Shape.MovImm8,
            0xC7 => Shape.MovImmZ,
            0xC8 => Shape.Enter, // enter imm16, imm8
            0xC9 => Shape.None, // leave
            0xCC => Shape.None, // int3, which is what sits between functions

            >= 0xD0 and <= 0xD3 => Shape.ModRm, // the shift group, by 1 or by cl

            // The x87 escapes always have a ModRM, whether or not it points at memory, so the ModRM decode gives the
            // right length.
            >= 0xD8 and <= 0xDF => Shape.ModRm,

            >= 0xE0 and <= 0xE3 => Shape.RelImm8, // loop and jrcxz

            // call and jmp near. Vendors disagree on whether a `66` prefix narrows the displacement to 2 bytes, so that
            // combination is refused.
            0xE8 or 0xE9 => operand16 ? Shape.Refused : Shape.RelImmZ,

            0xEB => Shape.RelImm8, // jmp rel8

            0xF6 => Shape.Group3Byte,
            0xF7 => Shape.Group3Word,
            0xFE => Shape.ModRm, // inc and dec on a byte
            0xFF => Shape.Group5,

            _ => Shape.Refused,
        };
    }

    private static Shape TwoByteShape(byte opcode, bool operand16)
    {
        return opcode switch
        {
            0x05 => Shape.None, // syscall
            0x0B => Shape.None, // ud2
            0x0D => Shape.ModRm, // the prefetch group, which Mono's collector opens functions with

            // The sse moves and the hint group, where the multi byte nops live.
            >= 0x10 and <= 0x1F => Shape.ModRm,

            >= 0x28 and <= 0x2F => Shape.ModRm, // movaps, the converts, comiss
            0x31 => Shape.None, // rdtsc
            >= 0x40 and <= 0x6F => Shape.ModRm, // cmovcc and the packed arithmetic

            >= 0x70 and <= 0x73 => Shape.ModRmImm8, // pshuf and the packed shift groups
            >= 0x74 and <= 0x76 => Shape.ModRm, // pcmpeq
            >= 0x7C and <= 0x7F => Shape.ModRm, // hadd, movq, movdqa

            // jcc near, refused with a `66` prefix for the same reason as call and jmp near.
            >= 0x80 and <= 0x8F => operand16 ? Shape.Refused : Shape.RelImmZ,

            >= 0x90 and <= 0x9F => Shape.ModRm, // setcc
            0xA2 => Shape.None, // cpuid
            0xA3 => Shape.ModRm, // bt
            0xA4 => Shape.ModRmImm8, // shld by imm8
            0xA5 => Shape.ModRm, // shld by cl
            0xAB => Shape.ModRm, // bts
            0xAC => Shape.ModRmImm8, // shrd by imm8
            0xAD => Shape.ModRm, // shrd by cl
            0xAE => Shape.ModRm, // the fence and save group
            0xAF => Shape.ModRm, // imul

            >= 0xB0 and <= 0xB7 => Shape.ModRm, // cmpxchg, btr, movzx
            0xBA => Shape.ModRmImm8, // the bit test group
            >= 0xBB and <= 0xBF => Shape.ModRm, // btc, bsf, bsr, movsx

            0xC0 or 0xC1 => Shape.ModRm, // xadd
            0xC2 => Shape.ModRmImm8, // cmpps
            0xC3 => Shape.ModRm, // movnti
            >= 0xC4 and <= 0xC6 => Shape.ModRmImm8, // pinsrw, pextrw, shufps
            0xC7 => Shape.ModRm, // the cmpxchg8b group
            >= 0xC8 and <= 0xCF => Shape.None, // bswap

            >= 0xD0 and <= 0xFE => Shape.ModRm, // the rest of the packed arithmetic

            _ => Shape.Refused,
        };
    }

    // Describes what follows the opcode. The group entries need the ModRM reg field before their immediate is known,
    // because the same opcode has one for some reg values and none for others.
    private enum Shape
    {
        Refused,
        None,
        Imm8,
        Imm16,
        ImmZ,
        ImmV,
        Enter,
        MovOffset,
        ModRm,
        ModRmImm8,
        ModRmImmZ,
        RelImm8,
        RelImmZ,
        Group3Byte,
        Group3Word,
        MovImm8,
        MovImmZ,
        Group5,
    }
}
