using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Represents a watcher over consecutive values of type <typeparamref name="T"/> at the end of a pointer path,
///     read as 1 array.
/// </summary>
/// <remarks>
///     The length is fixed when the watcher is created, because a native array doesn't store its length.
/// </remarks>
/// <typeparam name="T">The unmanaged element type.</typeparam>
public sealed class ArrayWatcher<T> : Watcher<T[]>
    where T : unmanaged
{
    private readonly Address _address;
    private readonly int _length;
    private readonly long[] _offsets;
    private readonly IMemoryReader _reader;

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
        : base(ticks, ContentsComparer<T>.Instance)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(offsets);

        _reader = reader;
        _length = length;
        _address = address;
        _offsets = offsets;
    }

    /// <inheritdoc/>
    protected override Result<T[]> Read()
    {
        if (!_reader.Deref(_address, _offsets).TryUnwrap(out var address, out var error))
        {
            return error;
        }

        return _reader.ReadArray<T>(address, _length);
    }

    /// <summary>
    ///     Formats the watcher as the element type, the length and its pointer path.
    /// </summary>
    /// <returns>
    ///     The formatted watcher, as in <c>Int32[4] at 0x1000 0x10</c>.
    /// </returns>
    public override string ToString()
    {
        return $"{typeof(T).Name}[{_length}] at {PointerPathFormatter.Format(_address, _offsets)}";
    }
}

// Compares arrays element by element, because every read creates a new array and the default comparer would
// report a change on every tick.
file sealed class ContentsComparer<T> : IEqualityComparer<T[]?>
    where T : unmanaged
{
    public static readonly ContentsComparer<T> Instance = new();

    public bool Equals(T[]? x, T[]? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null || x.Length != y.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < x.Length; i++)
        {
            if (!comparer.Equals(x[i], y[i]))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(T[]? obj)
    {
        if (obj is null)
        {
            return 0;
        }

        var comparer = EqualityComparer<T>.Default;
        int hash = 17;
        foreach (var element in obj)
        {
            hash = unchecked((hash * 31) + comparer.GetHashCode(element));
        }

        return hash;
    }
}
