using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Represents a value of type <typeparamref name="T"/> that is read at most once per tick and compared across reads.
/// </summary>
/// <remarks>
///     Derive from it and override <see cref="Read"/> for a watcher over any source, such as a delegate or another
///     watcher. A read that makes a new object each time needs a comparer, because the default comparer compares
///     such objects by reference and reports a change on every read.
/// </remarks>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
/// <param name="comparer">The comparer for 2 reads, or <see langword="null"/> for the type's default comparer.</param>
public abstract class Watcher<T>(
    ITickCounter ticks,
    IEqualityComparer<T?>? comparer = null) : Watcher(ticks)
{
    // Reads IsValueType once, because it's a reflection call on .NET Framework.
    private static readonly bool _isValueType = typeof(T).IsValueType;

    private readonly IEqualityComparer<T?> _comparer = comparer ?? EqualityComparer<T?>.Default;

    // Marks a struct whose equal values can keep 1 box though their bits differ, like 2 equal strings in a struct. A
    // primitive keeps its box only with equal bits, because -0.0 equals 0.0 but has to box as -0.0.
    private readonly bool _boxesEqualValues = (comparer is null || comparer == EqualityComparer<T?>.Default)
        && typeof(T).IsValueType && !typeof(T).IsPrimitive && !typeof(T).IsEnum;

    private T? _current;
    private object? _currentBox;
    private bool _filled;
    private T? _old;
    private object? _oldBox;

    /// <summary>
    ///     Gets the value from the latest read.
    /// </summary>
    public new T? Current
    {
        get
        {
            EnsureFresh();
            return _current;
        }
    }

    /// <summary>
    ///     Gets the value from the read before the latest one, however many ticks ago it was.
    /// </summary>
    public new T? Old
    {
        get
        {
            EnsureFresh();
            return _old;
        }
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
        return Changed && _comparer.Equals(_current, value);
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
        return Changed && _comparer.Equals(_old, value);
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
        return compare(_old, _current);
    }

    /// <summary>
    ///     Reads the value.
    /// </summary>
    /// <returns>
    ///     The value on success, otherwise a failed result.
    /// </returns>
    protected abstract Result<T> Read();

    private static bool HasSameBits(T? x, T? y)
    {
        ref byte xBytes = ref Unsafe.As<T?, byte>(ref x);
        ref byte yBytes = ref Unsafe.As<T?, byte>(ref y);

        for (int i = 0; i < Unsafe.SizeOf<T?>(); i++)
        {
            if (Unsafe.Add(ref xBytes, i) != Unsafe.Add(ref yBytes, i))
            {
                return false;
            }
        }

        return true;
    }

    private protected sealed override object? BoxCurrent()
    {
        // Boxes a value type once per value. A reference type is already an object.
        return _isValueType ? _currentBox ??= _current : _current;
    }

    private protected sealed override object? BoxOld()
    {
        return _isValueType ? _oldBox ??= _old : _old;
    }

    private protected sealed override void Refresh()
    {
        Result<T> result;
        bool changed;
        try
        {
            result = Read();

            // Compare before the values move, because a comparer that throws then leaves the values to FailAction.
            changed = result.IsOk && _filled && !_comparer.Equals(_current, result.Value);
        }
        catch (Exception ex)
        {
            Fail(new ExceptionError(ex));
            throw;
        }

        if (result.TryUnwrap(out var value, out var error))
        {
            bool filled = _filled;
            _old = _current;
            _oldBox = _currentBox;
            _current = value;

            // Keep the box of an equal value, because untyped access would box it again. A custom comparer can take
            // different values as equal, so only equal bits or the default comparer on a struct keep the box.
            bool keepsBox = HasSameBits(_old, _current) || (_boxesEqualValues && filled && !changed);
            if (!keepsBox)
            {
                _currentBox = null;
            }

            Changed = changed;
            _filled = true;
            Error = null;

            return;
        }

        Fail(error);

        if (FailAction == FailAction.Throw)
        {
            throw new InvalidOperationException(
                SR.FormatWatcherReadFailed(this, error.Message));
        }
    }

    private void Fail(ResultError error)
    {
        Error = error;
        Changed = false;

        if (FailAction == FailAction.Clear)
        {
            _old = default;
            _oldBox = null;
            _current = default;
            _currentBox = null;
            _filled = false;
        }
    }
}
