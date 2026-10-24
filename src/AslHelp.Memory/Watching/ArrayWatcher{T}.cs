using System;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Represents a watcher over consecutive values of type <typeparamref name="T"/> at the end of a pointer path,
///     read as 1 array.
/// </summary>
/// <remarks>
///     The length is fixed when the watcher is created, because a native array doesn't store its length.
/// </remarks>
/// <typeparam name="T">The unmanaged element type, other than <see langword="nint"/> or <see langword="nuint"/>.</typeparam>
public sealed partial class ArrayWatcher<T> : Watcher<T[]>
    where T : unmanaged
{
    private readonly int _length;
    private readonly Address _address;
    private readonly long[] _offsets;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ArrayWatcher{T}"/> class.
    /// </summary>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="length">The number of elements to read.</param>
    /// <param name="address">The address the path starts at.</param>
    /// <param name="offsets">The offset to add after each pointer read.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/>, <paramref name="reader"/> or <paramref name="offsets"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="length"/> is negative.
    /// </exception>
    public ArrayWatcher(ITickCounter ticks, IMemoryReader reader, int length, Address address, params long[] offsets)
        : base(ticks, reader, ContentsComparer.Instance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(offsets);

        _length = length;
        _address = address;
        _offsets = offsets;
    }

    /// <inheritdoc/>
    protected override Result<T[]> Read(IMemoryReader reader)
    {
        if (!reader.Deref(_address, _offsets).TryUnwrap(out var address, out var error))
        {
            return error;
        }

        return reader.ReadArray<T>(address, _length);
    }

    /// <summary>
    ///     Formats the watcher as the element type, the length and its pointer path.
    /// </summary>
    /// <returns>
    ///     The formatted watcher, as in <c>Int32[4] at 0x1000 0x10</c>.
    /// </returns>
    public override string ToString()
    {
        return $"{typeof(T).Name}[{_length}] at {FormatPath(_address, _offsets)}";
    }
}
