using System;
using System.Buffers.Binary;
using System.Text;

namespace AslHelp.Memory;

public static partial class MemoryReaderExtensions
{
    /// <summary>
    ///     The number of units <c>ReadTerminatedString</c> reads before the terminator when no maximum is given.
    /// </summary>
    public const int DefaultMaxChars = 512;

    private const int ChunkUnits = 64;

    extension(IMemoryReader reader)
    {
        /// <summary>
        ///     Reads a string of <paramref name="length"/> units at <paramref name="address"/>, cut at the first
        ///     terminator inside it.
        /// </summary>
        /// <param name="address">The address of the first character.</param>
        /// <param name="length">
        ///     The number of units to read, counted in bytes for <see cref="StringType.Ascii"/> and
        ///     <see cref="StringType.Utf8"/> and in chars for <see cref="StringType.Utf16"/>.
        /// </param>
        /// <param name="type">The encoding of the string.</param>
        /// <returns>
        ///     The string on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="length"/> is negative or <paramref name="type"/> isn't a defined value.
        /// </exception>
        public Result<string> ReadString(Address address, int length, StringType type)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegative(length);

            int unit = UnitSize(type);
            var bytes = new byte[length * unit];

            var read = reader.Read(address, bytes);
            if (read.IsErr)
            {
                return read.Error;
            }

            int units = IndexOfTerminator(bytes, unit);
            return Decode(bytes, units == -1 ? length : units, type, reader.Layout.ByteOrder);
        }

        /// <summary>
        ///     Reads a string at <paramref name="address"/> up to its terminator.
        /// </summary>
        /// <remarks>
        ///     The read goes in chunks and halves a chunk that isn't readable, so a string that ends close to the end of
        ///     readable memory still reads.
        /// </remarks>
        /// <param name="address">The address of the first character.</param>
        /// <param name="type">The encoding of the string.</param>
        /// <param name="maxLength">
        ///     The largest number of units before the terminator, counted in bytes for <see cref="StringType.Ascii"/>
        ///     and <see cref="StringType.Utf8"/> and in chars for <see cref="StringType.Utf16"/>.
        /// </param>
        /// <returns>
        ///     The string on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="maxLength"/> is negative or <paramref name="type"/> isn't a defined value.
        /// </exception>
        public Result<string> ReadTerminatedString(Address address, StringType type, int maxLength = DefaultMaxChars)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegative(maxLength);

            int unit = UnitSize(type);

            // Reserves room for the terminator after the longest accepted string.
            var bytes = new byte[(maxLength + 1) * unit];
            int filled = 0;
            int chunk = ChunkUnits;

            while (filled < bytes.Length)
            {
                int size = Math.Min(chunk * unit, bytes.Length - filled);
                var slice = bytes.AsSpan(filled, size);

                var read = reader.Read(address + filled, slice);
                if (read.IsErr)
                {
                    if (chunk == 1)
                    {
                        return read.Error;
                    }

                    chunk /= 2;
                    continue;
                }

                int found = IndexOfTerminator(slice, unit);
                if (found != -1)
                {
                    return Decode(bytes, (filled / unit) + found, type, reader.Layout.ByteOrder);
                }

                filled += size;
            }

            return MemoryError.TerminatorNotFound(address, maxLength);
        }
    }

    private static int UnitSize(StringType type)
    {
        return type switch
        {
            StringType.Ascii or StringType.Utf8 => sizeof(byte),
            StringType.Utf16 => sizeof(char),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The string type isn't defined."),
        };
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

    private static string Decode(byte[] bytes, int units, StringType type, ByteOrder order)
    {
        if (type != StringType.Utf16)
        {
            var encoding = type == StringType.Ascii ? Encoding.ASCII : Encoding.UTF8;
            return encoding.GetString(bytes, 0, units);
        }

        var chars = new char[units];
        for (int i = 0; i < units; i++)
        {
            var span = bytes.AsSpan(i * sizeof(char), sizeof(char));
            chars[i] = (char)(order == ByteOrder.LittleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(span)
                : BinaryPrimitives.ReadUInt16BigEndian(span));
        }

        return new string(chars);
    }
}
