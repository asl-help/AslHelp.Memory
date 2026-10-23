using System;

namespace AslHelp.Memory;

/// <summary>
///     Represents a failed read or write of a target's memory.
/// </summary>
internal sealed record MemoryError : ResultError
{
    private MemoryError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for <paramref name="size"/> bytes at <paramref name="address"/> that couldn't be read.
    /// </summary>
    /// <param name="address">The address of the first byte.</param>
    /// <param name="size">The number of bytes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError Unreadable(Address address, int size)
    {
        return new($"Couldn't read {size} bytes at {address}.");
    }

    /// <summary>
    ///     Creates the error for <paramref name="size"/> bytes at <paramref name="address"/> that couldn't be written.
    /// </summary>
    /// <param name="address">The address of the first byte.</param>
    /// <param name="size">The number of bytes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError Unwritable(Address address, int size)
    {
        return new($"Couldn't write {size} bytes at {address}.");
    }

    /// <summary>
    ///     Creates the error for a layout whose pointer size is outside 1 to 8 bytes.
    /// </summary>
    /// <param name="pointerSize">The pointer size of the layout.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError PointerSizeOutOfRange(byte pointerSize)
    {
        return new($"A pointer size of {pointerSize} bytes is outside 1 to 8 bytes.");
    }

    /// <summary>
    ///     Creates the error for <paramref name="type"/>, whose size depends on the host.
    /// </summary>
    /// <param name="type">The host-sized type.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError HostSizedType(Type type)
    {
        return new($"'{type.Name}' is as wide as a pointer on the host, not in the target. Use ReadPointer instead.");
    }

    /// <summary>
    ///     Creates the error for a string at <paramref name="address"/> with no terminator within
    ///     <paramref name="maxLength"/> characters.
    /// </summary>
    /// <param name="address">The address of the string.</param>
    /// <param name="maxLength">The bound the read searched within.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError TerminatorNotFound(Address address, int maxLength)
    {
        return new($"The string at {address} has no terminator within {maxLength} characters.");
    }
}
