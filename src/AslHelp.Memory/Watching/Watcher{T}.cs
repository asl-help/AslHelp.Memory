using System;
using System.Collections.Generic;
using System.Text;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Represents a value of type <typeparamref name="T"/> read from a target's memory and compared across reads.
/// </summary>
/// <remarks>
///     A read that makes a new object each time needs a comparer, because the default comparer compares such objects
///     by reference and reports a change on every read.
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
/// <param name="reader">The memory to read from.</param>
/// <param name="comparer">The comparer for 2 reads, or <see langword="null"/> for the type's default comparer.</param>
public abstract class Watcher<T>(
    ITickCounter ticks,
    IMemoryReader reader,
    IEqualityComparer<T?>? comparer = null) : Watcher(ticks, reader)
{
    private readonly IEqualityComparer<T?> _comparer = comparer ?? EqualityComparer<T?>.Default;

    private bool _filled;

    /// <summary>
    ///     Gets the value from the read before the latest one, however many ticks ago it was.
    /// </summary>
    public T? Old
    {
        get
        {
            EnsureFresh();
            return field;
        }
        private set;
    }

    /// <summary>
    ///     Gets the value from the latest read.
    /// </summary>
    public T? Current
    {
        get
        {
            EnsureFresh();
            return field;
        }
        private set;
    }

    /// <summary>
    ///     Gets a value indicating whether the latest read differs from the one before it. The first successful read
    ///     only fills the values and isn't a change.
    /// </summary>
    public bool Changed
    {
        get
        {
            EnsureFresh();
            return field;
        }
        private set;
    }

    /// <summary>
    ///     Returns whether the value changed and the latest one equals <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The value to compare the latest one to.</param>
    /// <returns>
    ///     <see langword="true"/> when the value changed to <paramref name="value"/>.
    /// </returns>
    public bool ChangedTo(T value)
    {
        return Changed && _comparer.Equals(Current, value);
    }

    /// <summary>
    ///     Returns whether the value changed and the previous one equals <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The value to compare the previous one to.</param>
    /// <returns>
    ///     <see langword="true"/> when the value changed from <paramref name="value"/>.
    /// </returns>
    public bool ChangedFrom(T value)
    {
        return Changed && _comparer.Equals(Old, value);
    }

    /// <summary>
    ///     Runs <paramref name="compare"/> on the previous and the latest value.
    /// </summary>
    /// <param name="compare">The comparison, which gets the previous value and then the latest one.</param>
    /// <returns>
    ///     The result of <paramref name="compare"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="compare"/> is <see langword="null"/>.
    /// </exception>
    public bool Check(Func<T?, T?, bool> compare)
    {
        ArgumentNullException.ThrowIfNull(compare);

        EnsureFresh();
        return compare(Old, Current);
    }

    /// <summary>
    ///     Reads the value from <paramref name="reader"/>.
    /// </summary>
    /// <param name="reader">The memory to read from.</param>
    /// <returns>
    ///     The value on success, otherwise a failed result.
    /// </returns>
    protected abstract Result<T> Read(IMemoryReader reader);

    /// <summary>
    ///     Formats a pointer path as its address followed by each offset, as in <c>0x1000 0x10 -0x8</c>.
    /// </summary>
    /// <param name="address">The address the path starts at.</param>
    /// <param name="offsets">The offsets after each pointer read.</param>
    /// <returns>
    ///     The formatted path.
    /// </returns>
    private protected static string FormatPath(Address address, long[] offsets)
    {
        StringBuilder path = new(address.ToString());
        foreach (long offset in offsets)
        {
            // Negates in unchecked ulong, because negating long.MinValue overflows.
            ulong magnitude = offset < 0 ? unchecked(0UL - (ulong)offset) : (ulong)offset;
            path.Append(offset < 0 ? " -" : " ").Append(new Address(magnitude));
        }

        return path.ToString();
    }

    private protected sealed override void Skip()
    {
        Changed = false;
    }

    private protected sealed override void Refresh(IMemoryReader reader)
    {
        if (Read(reader).TryUnwrap(out var value, out var error))
        {
            Old = Current;
            Current = value;
            Changed = _filled && !_comparer.Equals(Old, Current);
            _filled = true;

            return;
        }

        if (FailAction == FailAction.Throw)
        {
            throw new InvalidOperationException(
                $"Couldn't read {this}. {error.Message} Set FailAction to Clear or KeepOld when the value isn't always there.");
        }

        Changed = false;

        if (FailAction == FailAction.Clear)
        {
            Old = default;
            Current = default;
            _filled = false;
        }
    }
}
