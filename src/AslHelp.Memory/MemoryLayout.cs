using System;

namespace AslHelp.Memory;

/// <summary>
///     Represents the pointer size and byte order of a target's memory.
/// </summary>
/// <remarks>
///     <c>default(MemoryLayout)</c> has a pointer size of 0, which every pointer read refuses.
/// </remarks>
public readonly record struct MemoryLayout
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MemoryLayout"/> struct.
    /// </summary>
    /// <param name="pointerSize">The size of a pointer in bytes, from 1 to 8.</param>
    /// <param name="byteOrder">The order of the bytes of a multi-byte value.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="pointerSize"/> is 0 or greater than 8.
    /// </exception>
    public MemoryLayout(byte pointerSize, ByteOrder byteOrder)
    {
        if (pointerSize is < MinPointerSize or > MaxPointerSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pointerSize), pointerSize, "A pointer is 1 to 8 bytes wide.");
        }

        PointerSize = pointerSize;
        ByteOrder = byteOrder;
    }

    /// <summary>
    ///     The smallest pointer size in bytes.
    /// </summary>
    public const byte MinPointerSize = 1;

    /// <summary>
    ///     The largest pointer size in bytes.
    /// </summary>
    public const byte MaxPointerSize = sizeof(ulong);

    /// <summary>
    ///     Gets the layout of a 32-bit x86 target.
    /// </summary>
    public static MemoryLayout X86 { get; } = new(4, ByteOrder.LittleEndian);

    /// <summary>
    ///     Gets the layout of a 64-bit x86 target.
    /// </summary>
    public static MemoryLayout X64 { get; } = new(8, ByteOrder.LittleEndian);

    /// <summary>
    ///     Gets the layout of a 32-bit big-endian target.
    /// </summary>
    public static MemoryLayout BigEndian32 { get; } = new(4, ByteOrder.BigEndian);

    /// <summary>
    ///     Gets the size of a pointer in bytes.
    /// </summary>
    public byte PointerSize { get; }

    /// <summary>
    ///     Gets the order of the bytes of a multi-byte value.
    /// </summary>
    public ByteOrder ByteOrder { get; }
}
