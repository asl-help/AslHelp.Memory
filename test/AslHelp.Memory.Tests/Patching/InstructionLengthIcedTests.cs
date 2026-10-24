using AslHelp.Memory.Patching;
using Iced.Intel;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Patching;

[TestFixture]
public class InstructionLengthIcedTests
{
    private const int Filler = 0x11;

    private static readonly byte[][] _prefixes = [[], [0x66], [0x67], [0xF3], [0x48], [0x41], [0x66, 0x48]];

    // Covers register, disp8, disp32, SIB, SIB with no base and the rip relative or absolute form.
    private static readonly byte[][] _operands =
    [
        [0xC0],
        [0xC8],
        [0x00],
        [0x05, 0x78, 0x56, 0x34, 0x12],
        [0x45, 0x08],
        [0x80, 0x78, 0x56, 0x34, 0x12],
        [0x04, 0x24],
        [0x44, 0x24, 0x08],
        [0x04, 0x25, 0x78, 0x56, 0x34, 0x12],
        [0x38],
        [0xF8],
    ];

    // Every encoding the decoder accepts has the length and position dependence Iced decodes for it.
    [TestCase(true)]
    [TestCase(false)]
    public void TryDecode_EveryAcceptedEncoding_AgreesWithIced(bool is64Bit)
    {
        List<string> mismatches = [];
        int compared = 0;

        foreach (byte[] prefix in _prefixes)
        {
            if (!is64Bit && prefix is [.., >= 0x40 and <= 0x4F])
            {
                continue;
            }

            for (int opcode = 0; opcode <= 0xFF; opcode++)
            {
                foreach (byte[] operand in _operands)
                {
                    compared += Compare([.. prefix, (byte)opcode, .. operand], is64Bit, mismatches);
                    compared += Compare([.. prefix, 0x0F, (byte)opcode, .. operand], is64Bit, mismatches);
                }
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mismatches, Is.Empty);
            Assert.That(compared, Is.GreaterThan(10_000));
        }
    }

    // Returns 1 when the encoding was compared, because the decoder refused or Iced found it invalid otherwise.
    private static int Compare(byte[] encoding, bool is64Bit, List<string> mismatches)
    {
        byte[] code = new byte[encoding.Length + 16];
        code.AsSpan().Fill(Filler);
        encoding.CopyTo(code, 0);

        if (!InstructionLength.TryDecode(code, is64Bit, out int length, out bool isRelative))
        {
            return 0;
        }

        var decoder = Decoder.Create(is64Bit ? 64 : 32, new ByteArrayCodeReader(code));
        decoder.IP = 0x1000;
        decoder.Decode(out var instruction);

        bool icedRelative = instruction.IsIPRelativeMemoryOperand
            || instruction.Op0Kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64;

        // Skips the register forms of memory-only opcodes, because no compiler emits them and their length is still
        // the one the decoder reports.
        if (instruction.IsInvalid)
        {
            return 0;
        }

        if (instruction.Length != length || icedRelative != isRelative)
        {
            mismatches.Add(
                $"{BitConverter.ToString(code, 0, Math.Max(length, instruction.Length))}: decoder {length}/{isRelative}, "
                + $"Iced {instruction.Length}/{icedRelative}");
        }

        return 1;
    }
}
