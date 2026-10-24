using System;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Represents a watcher over a value of type <typeparamref name="T"/> at the end of a pointer path.
/// </summary>
/// <typeparam name="T">The unmanaged type to read, other than <see langword="nint"/> or <see langword="nuint"/>.</typeparam>
public sealed class MemoryWatcher<T> : Watcher<T>
    where T : unmanaged
{
    private readonly Address _address;
    private readonly long[] _offsets;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemoryWatcher{T}"/> class.
    /// </summary>
    /// <param name="ticks">The tick counter the watcher reads at most once per tick of.</param>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="address">The address the path starts at.</param>
    /// <param name="offsets">The offset to add after each pointer read.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/>, <paramref name="reader"/> or <paramref name="offsets"/> is <see langword="null"/>.
    /// </exception>
    public MemoryWatcher(ITickCounter ticks, IMemoryReader reader, Address address, params long[] offsets)
        : base(ticks, reader)
    {
        ArgumentNullException.ThrowIfNull(offsets);

        _address = address;
        _offsets = offsets;
    }

    /// <inheritdoc/>
    protected override Result<T> Read(IMemoryReader reader)
    {
        if (!reader.Deref(_address, _offsets).TryUnwrap(out var address, out var error))
        {
            return error;
        }

        return reader.Read<T>(address);
    }

    /// <summary>
    ///     Formats the watcher as the type it reads and its pointer path.
    /// </summary>
    /// <returns>
    ///     The formatted watcher, as in <c>Int32 at 0x1000 0x10</c>.
    /// </returns>
    public override string ToString()
    {
        return $"{typeof(T).Name} at {FormatPath(_address, _offsets)}";
    }
}
