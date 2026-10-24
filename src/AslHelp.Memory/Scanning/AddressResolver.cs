using AslHelp.Memory.Scanning.Errors;
using System;
using System.Buffers.Binary;

namespace AslHelp.Memory.Scanning;

/// <summary>
///     Represents the method that turns a scan match into another address, reading the target when needed.
/// </summary>
/// <param name="reader">The memory to read from.</param>
/// <param name="match">The address the pattern matched at.</param>
/// <returns>
///     The resolved address on success, otherwise a failed result.
/// </returns>
public delegate Result<Address> AddressResolver(IMemoryReader reader, Address match);

/// <summary>
///     Provides the common <see cref="AddressResolver"/>s.
/// </summary>
public static class AddressResolverExtensions
{
    extension(AddressResolver)
    {
        /// <summary>
        ///     Creates a resolver for an instruction that references a global.
        /// </summary>
        /// <remarks>
        ///     At a pointer size of 8 the resolver reads the signed 32-bit displacement at
        ///     <c>match + displacementOffset</c> and adds it to the end of the instruction, as RIP-relative addressing
        ///     does. At a pointer size of 4 the operand is the absolute address, read as an unsigned 32-bit number.
        ///     Any other pointer size fails.
        /// </remarks>
        /// <param name="displacementOffset">The number of bytes from the match to the displacement.</param>
        /// <param name="instructionSize">The number of bytes from the displacement to the end of the instruction.</param>
        /// <returns>
        ///     The resolver.
        /// </returns>
        public static AddressResolver Relative(int displacementOffset, int instructionSize = 4)
        {
            return (reader, match) =>
            {
                ArgumentNullException.ThrowIfNull(reader);

                var at = match + displacementOffset;

                return reader.Layout.PointerSize switch
                {
                    8 => ReadDisplacement(reader, at, instructionSize),
                    4 => ReadAbsolute(reader, at),
                    var size => ScanError.UnsupportedPointerSize(size),
                };
            };
        }

        /// <summary>
        ///     Creates a resolver for a signed 32-bit distance from the end of an instruction, as a near call or jump
        ///     encodes it at any pointer size.
        /// </summary>
        /// <param name="displacementOffset">The number of bytes from the match to the displacement.</param>
        /// <param name="instructionSize">The number of bytes from the displacement to the end of the instruction.</param>
        /// <returns>
        ///     The resolver.
        /// </returns>
        public static AddressResolver Displacement(int displacementOffset, int instructionSize = 4)
        {
            return (reader, match) =>
            {
                ArgumentNullException.ThrowIfNull(reader);

                return ReadDisplacement(reader, match + displacementOffset, instructionSize);
            };
        }

        /// <summary>
        ///     Creates a resolver that moves the match by <paramref name="distance"/> bytes without reading.
        /// </summary>
        /// <param name="distance">The number of bytes to move by, negative to move back.</param>
        /// <returns>
        ///     The resolver.
        /// </returns>
        public static AddressResolver Offset(long distance)
        {
            return (_, match) => match + distance;
        }
    }

    private static Result<Address> ReadDisplacement(IMemoryReader reader, Address at, int instructionSize)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];

        var read = reader.Read(at, bytes);
        if (read.IsErr)
        {
            return read.Error;
        }

        int displacement = reader.Layout.ByteOrder == ByteOrder.BigEndian
            ? BinaryPrimitives.ReadInt32BigEndian(bytes)
            : BinaryPrimitives.ReadInt32LittleEndian(bytes);

        return at + ((long)instructionSize + displacement);
    }

    private static Result<Address> ReadAbsolute(IMemoryReader reader, Address at)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];

        var read = reader.Read(at, bytes);
        if (read.IsErr)
        {
            return read.Error;
        }

        uint address = reader.Layout.ByteOrder == ByteOrder.BigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes);

        return new Address(address);
    }
}
