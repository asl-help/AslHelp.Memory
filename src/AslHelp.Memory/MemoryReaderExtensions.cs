using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AslHelp.Memory;

/// <summary>
///     Provides typed reads over an <see cref="IMemoryReader"/> in the reader's layout.
/// </summary>
public static partial class MemoryReaderExtensions
{
    extension(IMemoryReader reader)
    {
        /// <summary>
        ///     Reads a value of type <typeparamref name="T"/> at <paramref name="address"/>.
        /// </summary>
        /// <remarks>
        ///     A primitive number, <see langword="char"/> or <see langword="bool"/> is read in the layout's byte order.
        ///     Any other struct is read as raw bytes with no byte order applied to its fields.
        /// </remarks>
        /// <typeparam name="T">The unmanaged type to read, other than <see langword="nint"/> or <see langword="nuint"/>.</typeparam>
        /// <param name="address">The address of the value.</param>
        /// <returns>
        ///     The value on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader is <see langword="null"/>.
        /// </exception>
        public Result<T> Read<T>(Address address)
            where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(reader);

            if (IsHostSized<T>())
            {
                return MemoryError.HostSizedType(typeof(T));
            }

            Span<T> value = stackalloc T[1];
            var bytes = MemoryMarshal.AsBytes(value);

            var read = reader.Read(address, bytes);
            if (read.IsErr)
            {
                return read.Error;
            }

            ApplyByteOrder<T>(bytes, reader.Layout.ByteOrder);
            return value[0];
        }

        /// <summary>
        ///     Reads <paramref name="count"/> consecutive values of type <typeparamref name="T"/> at
        ///     <paramref name="address"/>.
        /// </summary>
        /// <remarks>
        ///     Each element follows the same byte order rule as <c>Read</c>.
        /// </remarks>
        /// <typeparam name="T">The unmanaged element type, other than <see langword="nint"/> or <see langword="nuint"/>.</typeparam>
        /// <param name="address">The address of the first element.</param>
        /// <param name="count">The number of elements.</param>
        /// <returns>
        ///     The elements on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="count"/> is negative.
        /// </exception>
        public Result<T[]> ReadArray<T>(Address address, int count)
            where T : unmanaged
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (IsHostSized<T>())
            {
                return MemoryError.HostSizedType(typeof(T));
            }

            if (count == 0)
            {
                return Array.Empty<T>();
            }

            var values = new T[count];
            var bytes = MemoryMarshal.AsBytes(values.AsSpan());

            var read = reader.Read(address, bytes);
            if (read.IsErr)
            {
                return read.Error;
            }

            ApplyByteOrder<T>(bytes, reader.Layout.ByteOrder);
            return values;
        }

        /// <summary>
        ///     Reads a pointer of the layout's pointer size at <paramref name="address"/> in the layout's byte order.
        /// </summary>
        /// <param name="address">The address of the pointer.</param>
        /// <returns>
        ///     The zero-extended pointer on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader is <see langword="null"/>.
        /// </exception>
        public Result<Address> ReadPointer(Address address)
        {
            ArgumentNullException.ThrowIfNull(reader);

            var layout = reader.Layout;
            if (layout.PointerSize is < MemoryLayout.MinPointerSize or > MemoryLayout.MaxPointerSize)
            {
                return MemoryError.PointerSizeOutOfRange(layout.PointerSize);
            }

            Span<byte> bytes = stackalloc byte[layout.PointerSize];

            var read = reader.Read(address, bytes);
            if (read.IsErr)
            {
                return read.Error;
            }

            return new Address(Assemble(bytes, layout.ByteOrder));
        }

        /// <summary>
        ///     Follows a pointer chain from <paramref name="address"/>, reading a pointer and adding the next offset to
        ///     it at each step.
        /// </summary>
        /// <param name="address">The address of the first pointer.</param>
        /// <param name="offsets">The offset to add after each pointer read.</param>
        /// <returns>
        ///     The final address on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader or <paramref name="offsets"/> is <see langword="null"/>.
        /// </exception>
        public Result<Address> Deref(Address address, params long[] offsets)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(offsets);

            foreach (long offset in offsets)
            {
                var pointer = reader.ReadPointer(address);
                if (!pointer.TryUnwrap(out address, out var error))
                {
                    return error;
                }

                address += offset;
            }

            return address;
        }
    }

    private static bool IsHostSized<T>()
    {
        return typeof(T) == typeof(nint) || typeof(T) == typeof(nuint);
    }

    private static ByteOrder HostByteOrder => BitConverter.IsLittleEndian ? ByteOrder.LittleEndian : ByteOrder.BigEndian;

    private static void ApplyByteOrder<T>(Span<byte> bytes, ByteOrder order)
        where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        if (order == HostByteOrder || size == 1 || !typeof(T).IsPrimitive)
        {
            return;
        }

        for (int i = 0; i < bytes.Length; i += size)
        {
            bytes.Slice(i, size).Reverse();
        }
    }

    private static ulong Assemble(ReadOnlySpan<byte> bytes, ByteOrder order)
    {
        ulong value = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            int shift = order == ByteOrder.LittleEndian ? i : bytes.Length - 1 - i;
            value |= (ulong)bytes[i] << (shift * 8);
        }

        return value;
    }
}
