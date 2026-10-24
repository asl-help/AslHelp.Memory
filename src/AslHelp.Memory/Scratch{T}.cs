using System;
using System.Buffers;

namespace AslHelp.Memory;

/// <summary>
///     Represents scratch space of an exact length, taken from the caller's stack when it fits and from the shared pool
///     otherwise.
/// </summary>
/// <typeparam name="T">The type of the elements.</typeparam>
internal readonly ref struct Scratch<T>
{
    private readonly T[]? _rented;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Scratch{T}"/> struct.
    /// </summary>
    /// <param name="stack">The stack space to use when <paramref name="length"/> fits in it.</param>
    /// <param name="length">The number of elements the scratch space spans.</param>
    public Scratch(Span<T> stack, int length)
    {
        if (length <= stack.Length)
        {
            _rented = null;
            Buffer = stack[..length];
        }
        else
        {
            _rented = ArrayPool<T>.Shared.Rent(length);
            Buffer = _rented.AsSpan(0, length);
        }
    }

    /// <summary>
    ///     Gets the scratch space, exactly as long as requested.
    /// </summary>
    public Span<T> Buffer { get; }

    /// <summary>
    ///     Gets a value indicating whether the scratch space came from the shared pool.
    /// </summary>
    public bool IsRented => _rented is not null;

    /// <summary>
    ///     Returns the rented array to the shared pool.
    /// </summary>
    public void Dispose()
    {
        if (_rented is not null)
        {
            ArrayPool<T>.Shared.Return(_rented);
        }
    }
}
