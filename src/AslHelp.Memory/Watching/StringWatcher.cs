using System;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Represents a watcher over a string at the end of a pointer path, cut at its first terminator.
/// </summary>
public sealed class StringWatcher : Watcher<string>
{
    private readonly Address _address;
    private readonly int _length;
    private readonly long[] _offsets;
    private readonly IMemoryReader _reader;
    private readonly StringType _type;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StringWatcher"/> class.
    /// </summary>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="length">
    ///     The number of units to read, counted in bytes for <see cref="StringType.Ascii"/> and
    ///     <see cref="StringType.Utf8"/> and in chars for <see cref="StringType.Utf16"/>.
    /// </param>
    /// <param name="type">The encoding of the string.</param>
    /// <param name="address">The address the path starts at.</param>
    /// <param name="offsets">The offset to add after each pointer read.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/>, <paramref name="reader"/> or <paramref name="offsets"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="length"/> is negative.
    /// </exception>
    public StringWatcher(
        ITickCounter ticks,
        IMemoryReader reader,
        int length,
        StringType type,
        Address address,
        params long[] offsets)
        : base(ticks)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(offsets);

        _reader = reader;
        _length = length;
        _type = type;
        _address = address;
        _offsets = offsets;
    }

    /// <inheritdoc/>
    protected override Result<string> Read()
    {
        if (!_reader.Deref(_address, _offsets).TryUnwrap(out var address, out var error))
        {
            return error;
        }

        return _reader.ReadString(address, _length, _type);
    }

    /// <summary>
    ///     Formats the watcher as the encoding it reads and its pointer path.
    /// </summary>
    /// <returns>
    ///     The formatted watcher, as in <c>Utf8 string at 0x1000 0x10</c>.
    /// </returns>
    public override string ToString()
    {
        return $"{_type} string at {PointerPathFormatter.Format(_address, _offsets)}";
    }
}
