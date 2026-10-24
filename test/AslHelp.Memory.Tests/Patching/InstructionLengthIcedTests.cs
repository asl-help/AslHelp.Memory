using AslHelp.Memory.Patching;
using Iced.Intel;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Patching;

[TestFixture]
public class InstructionLengthIcedTests
{

    private static readonly byte[] _fillers = [0x11, 0x00, 0x78, 0xC0, 0xFF];

    // Covers the escapes into the maps the decoder refuses, because a valid instruction behind one has a length the
    // 1- and 2-byte maps don't predict.
    private static readonly byte[][] _escapes =
    [
        [0x0F],
        [0x0F, 0x38],
        [0x0F, 0x3A],
        [0x0F, 0x0F],
        [0x8F, 0xE8, 0x78],
        [0x8F, 0xE9, 0x78],
        [0x8F, 0xEA, 0x78],
        [0xC5, 0xF8],
        [0xC4, 0xE2, 0x71],
        [0xC4, 0xE3, 0x71],
        [0x62, 0xF1, 0x7C, 0x48],
    ];

    private static readonly byte[][] _prefixes =
    [
        [],
        [0x66],
        [0x67],
        [0xF2],
        [0xF3],
        [0xF0],
        [0x2E],
        [0x64],
        [0x65],
        [0x48],
        [0x41],
        [0x66, 0x48],
        [0x66, 0x67],
        [0xF3, 0x66],
        [0x48, 0x66],
        [0x48, 0x48],
    ];

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
        [0x44, 0x25, 0x08],
        [0x84, 0x25, 0x78, 0x56, 0x34, 0x12],
        [0x06, 0x34, 0x12],
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
                    foreach (byte[] escape in _escapes)
                    {
                        compared += Compare([.. prefix, .. escape, (byte)opcode, .. operand], is64Bit, mismatches);
                    }
                }
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mismatches, Is.Empty, string.Join(Environment.NewLine, mismatches));
            Assert.That(compared, Is.GreaterThan(10_000));
        }
    }

    // Returns 1 when the encoding was compared. Tries several bytes after the encoding, because the bytes after an
    // escape like XOP decide whether Iced reads it as valid. An encoding Iced finds invalid with every one of them is a
    // #UD form whose length the CPU never reads.
    private static int Compare(byte[] encoding, bool is64Bit, List<string> mismatches)
    {
        int compared = 0;

        foreach (byte filler in _fillers)
        {
            byte[] code = new byte[encoding.Length + 16];
            code.AsSpan().Fill(filler);
            encoding.CopyTo(code, 0);

            if (!InstructionLength.TryDecode(code, is64Bit, out int length, out bool isRelative))
            {
                return 0;
            }

            var decoder = Decoder.Create(is64Bit ? 64 : 32, new ByteArrayCodeReader(code));
            decoder.IP = 0x1000;
            decoder.Decode(out var instruction);

            if (instruction.IsInvalid)
            {
                continue;
            }

            bool icedRelative = instruction.IsIPRelativeMemoryOperand
                || instruction.Op0Kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64;

            if (instruction.Length != length || icedRelative != isRelative)
            {
                mismatches.Add(
                    $"{BitConverter.ToString(code, 0, Math.Max(length, instruction.Length))}: decoder {length}/{isRelative}, "
                    + $"Iced {instruction.Length}/{icedRelative}");
            }

            compared = 1;
        }

        return compared;
    }
}
