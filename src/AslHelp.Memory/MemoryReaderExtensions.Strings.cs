using AslHelp.Memory.Errors;
using System;
using System.Buffers.Binary;
using System.Text;

namespace AslHelp.Memory;

public static partial class MemoryReaderExtensions
{
    /// <summary>
    ///     The number of units the null-terminated <c>ReadString</c> reads before the terminator when no maximum is given.
    /// </summary>
    public const int DefaultMaxChars = 512;

    private const int ChunkUnits = 64;

    // Keeps a UTF-16 length in bytes within an int.
    private const int MaxUnits = int.MaxValue / sizeof(char);

    // Sized so a UTF-16 string of the default length with its terminator stays on the stack.
    private const int StackBytes = (DefaultMaxChars + 1) * sizeof(char);

    extension(IMemoryReader reader)
    {
        /// <summary>
        ///     Reads a string of <paramref name="length"/> units at <paramref name="address"/>, cut at the first
        ///     terminator inside it.
        /// </summary>
        /// <param name="address">The address of the first character.</param>
        /// <param name="length">
        ///     The number of units to read, counted in bytes for <see cref="StringType.Ascii"/> and
        ///     <see cref="StringType.Utf8"/>, in chars for <see cref="StringType.Utf16"/> and in the units of the
        ///     picked encoding for <see cref="StringType.Auto"/>.
        /// </param>
        /// <param name="type">The encoding of the string.</param>
        /// <returns>
        ///     The string on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="length"/> is negative or above <see cref="int.MaxValue"/> / 2, or <paramref name="type"/>
        ///     isn't a defined value.
        /// </exception>
        public Result<string> ReadString(Address address, int length, StringType type)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, MaxUnits);

            var order = reader.Layout.ByteOrder;

            if (type == StringType.Auto)
            {
                return ReadAuto(reader, address, length, order);
            }

            int unit = UnitSize(type);
            using var scratch = new Scratch<byte>(stackalloc byte[StackBytes], length * unit);
            var bytes = scratch.Buffer;

            var read = reader.Read(address, bytes);
            if (read.IsErr)
            {
                return read.Error;
            }

            int units = IndexOfTerminator(bytes, unit);
            return Decode(bytes, units == -1 ? length : units, type, order);
        }

        /// <summary>
        ///     Reads a string at <paramref name="address"/> up to its terminator.
        /// </summary>
        /// <remarks>
        ///     The read goes in chunks and halves a chunk that isn't readable, so a string that ends close to the end of
        ///     readable memory still reads. A string without a terminator within <paramref name="maxLength"/> units is cut
        ///     after <paramref name="maxLength"/> units.
        /// </remarks>
        /// <param name="address">The address of the first character.</param>
        /// <param name="type">The encoding of the string.</param>
        /// <param name="maxLength">
        ///     The largest number of units before the terminator, counted in chars for <see cref="StringType.Utf16"/>
        ///     and in bytes for the other types. <see cref="StringType.Auto"/> reads UTF-8.
        /// </param>
        /// <returns>
        ///     The string on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="maxLength"/> is negative or above <see cref="int.MaxValue"/> / 2 - 1, or
        ///     <paramref name="type"/> isn't a defined value.
        /// </exception>
        public Result<string> ReadString(Address address, StringType type, int maxLength = DefaultMaxChars)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxLength, MaxUnits - 1);

            // Read UTF-8, because a read without a length can't look ahead at bytes past the terminator.
            if (type == StringType.Auto)
            {
                type = StringType.Utf8;
            }

            int unit = UnitSize(type);
            var order = reader.Layout.ByteOrder;

            // Reserves room for the terminator after the longest accepted string.
            int end = (maxLength + 1) * unit;
            using var scratch = new Scratch<byte>(stackalloc byte[StackBytes], end);
            var bytes = scratch.Buffer;

            int filled = 0;
            int scanned = 0;
            int chunk = ChunkUnits;

            while (filled < end)
            {
                int size = Math.Min(chunk * unit, end - filled);

                var read = reader.Read(address + filled, bytes.Slice(filled, size));
                if (read.IsErr)
                {
                    // Halve the units of the failed read, because a read shorter than the chunk would fail again at
                    // the same size.
                    int units = size / unit;
                    if (units > 1)
                    {
                        chunk = units / 2;
                        continue;
                    }

                    // Cut a string that fills maxLength at the end of readable memory, because only its terminator is
                    // missing.
                    if (filled >= maxLength * unit)
                    {
                        return Decode(bytes, maxLength, type, order);
                    }

                    return read.Error;
                }

                filled += size;

                int found = IndexOfTerminator(bytes[scanned..filled], unit);
                if (found != -1)
                {
                    return Decode(bytes, (scanned / unit) + found, type, order);
                }

                scanned = filled;
            }

            return Decode(bytes, maxLength, type, order);
        }
    }

    private static int UnitSize(StringType type)
    {
        return type switch
        {
            StringType.Ascii or StringType.Utf8 => sizeof(byte),
            StringType.Utf16 => sizeof(char),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, SR.StringTypeNotDefined),
        };
    }

    private static Result<string> ReadAuto(IMemoryReader reader, Address address, int length, ByteOrder order)
    {
        // Looks at the first 2 chars, which take 4 bytes in UTF-16.
        const int Sniffed = 2 * sizeof(char);

        using var scratch = new Scratch<byte>(stackalloc byte[StackBytes], Math.Max(length * sizeof(char), Sniffed));
        var bytes = scratch.Buffer;

        int sniffed = length >= sizeof(char) ? Math.Max(length, Sniffed) : length;
        var read = reader.Read(address, bytes[..sniffed]);

        // Read only the length as UTF-8 when the 4 bytes run past readable memory, because a short string can end
        // there.
        if (read.IsErr && sniffed > length)
        {
            sniffed = length;
            read = reader.Read(address, bytes[..sniffed]);
        }

        if (read.IsErr)
        {
            return read.Error;
        }

        var type = LooksLikeUtf16(bytes[..sniffed], order) ? StringType.Utf16 : StringType.Utf8;
        int size = length * UnitSize(type);

        // Read the rest of a UTF-16 string only after the guess, because a UTF-8 string can end at the end of readable
        // memory.
        if (size > sniffed)
        {
            read = reader.Read(address + sniffed, bytes[sniffed..size]);
            if (read.IsErr)
            {
                return read.Error;
            }
        }

        bytes = bytes[..size];
        int units = IndexOfTerminator(bytes, UnitSize(type));
        return Decode(bytes, units == -1 ? length : units, type, order);
    }

    // Take UTF-16 when the first 2 chars have a 0 high byte and a non-zero low byte, because Latin text in UTF-16
    // looks like that and UTF-8 text never does before its terminator.
    private static bool LooksLikeUtf16(ReadOnlySpan<byte> bytes, ByteOrder order)
    {
        return order == ByteOrder.LittleEndian
            ? bytes is [not 0, 0, not 0, 0, ..]
            : bytes is [0, not 0, 0, not 0, ..];
    }

    private static int IndexOfTerminator(ReadOnlySpan<byte> bytes, int unit)
    {
        for (int i = 0; i + unit <= bytes.Length; i += unit)
        {
            if (bytes[i] == 0 && bytes[i + unit - 1] == 0)
            {
                return i / unit;
            }
        }

        return -1;
    }

    private static string Decode(ReadOnlySpan<byte> bytes, int units, StringType type, ByteOrder order)
    {
        if (type != StringType.Utf16)
        {
            var encoding = type == StringType.Ascii ? Encoding.ASCII : Encoding.UTF8;
            return encoding.GetString(bytes[..units]);
        }

        using var scratch = new Scratch<char>(stackalloc char[StackBytes / sizeof(char)], units);
        var chars = scratch.Buffer;
        for (int i = 0; i < units; i++)
        {
            var span = bytes.Slice(i * sizeof(char), sizeof(char));
            chars[i] = (char)(order == ByteOrder.LittleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span));
        }

        return chars.ToString();
    }
}
