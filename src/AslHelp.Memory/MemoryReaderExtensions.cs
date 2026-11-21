using AslHelp.Memory.Errors;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AslHelp.Memory;

/// <summary>
///     Provides typed reads over an <see cref="IMemoryReader"/> in the reader's layout.
/// </summary>
public static partial class MemoryReaderExtensions
{
    // The number of pointers a read keeps on the stack.
    private const int StackPointers = 8;

    extension(IMemoryReader reader)
    {
        /// <summary>
        ///     Reads a value of type <typeparamref name="T"/> at <paramref name="address"/>.
        /// </summary>
        /// <remarks>
        ///     A primitive number, <see langword="char"/> or <see langword="bool"/> is read in the layout's byte order.
        ///     <see langword="nint"/> and <see langword="nuint"/> are read as a zero-extended pointer of the layout's
        ///     pointer size, like <c>ReadPointer</c>. Any other struct is read as raw bytes with no byte order applied to
        ///     its fields.
        /// </remarks>
        /// <typeparam name="T">The unmanaged type to read.</typeparam>
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
                return reader.ReadPointer(address).TryUnwrap(out var pointer, out var error) ? ToHostSized<T>(pointer) : error;
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
        /// <typeparam name="T">The unmanaged element type.</typeparam>
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

            if (count == 0)
            {
                return Array.Empty<T>();
            }

            if (IsHostSized<T>())
            {
                return ReadHostSized<T>(reader, address, count);
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

            Span<Address> pointer = stackalloc Address[1];

            var read = ReadPointers(reader, address, pointer);
            if (read.IsErr)
            {
                return read.Error;
            }

            return pointer[0];
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

    // Reads pointers of the layout's size, because nint and nuint follow the host and not the target.
    private static Result<T[]> ReadHostSized<T>(IMemoryReader reader, Address address, int count)
        where T : unmanaged
    {
        using var scratch = new Scratch<Address>(stackalloc Address[StackPointers], count);
        var pointers = scratch.Buffer;

        var read = ReadPointers(reader, address, pointers);
        if (read.IsErr)
        {
            return read.Error;
        }

        var values = new T[count];
        for (int i = 0; i < count; i++)
        {
            if (!ToHostSized<T>(pointers[i]).TryUnwrap(out values[i], out var error))
            {
                return error;
            }
        }

        return values;
    }

    // Reads all pointers in 1 read, each zero-extended from the layout's pointer size.
    private static Result ReadPointers(IMemoryReader reader, Address address, Span<Address> pointers)
    {
        var layout = reader.Layout;
        if (layout.PointerSize is < MemoryLayout.MinPointerSize or > MemoryLayout.MaxPointerSize)
        {
            return new PointerSizeError(layout.PointerSize);
        }

        int size = layout.PointerSize;
        using var scratch = new Scratch<byte>(stackalloc byte[StackPointers * MemoryLayout.MaxPointerSize], checked(size * pointers.Length));
        var bytes = scratch.Buffer;

        var read = reader.Read(address, bytes);
        if (read.IsErr)
        {
            return read.Error;
        }

        for (int i = 0; i < pointers.Length; i++)
        {
            pointers[i] = new(Assemble(bytes.Slice(i * size, size), layout.ByteOrder));
        }

        return Result.Ok();
    }

    private static Result<T> ToHostSized<T>(Address pointer)
        where T : unmanaged
    {
        if (IntPtr.Size == sizeof(uint) && pointer.Value > uint.MaxValue)
        {
            return new HostSizedTypeError(typeof(T), pointer);
        }

        nuint value = (nuint)pointer.Value;
        return Unsafe.As<nuint, T>(ref value);
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
