using System;
using System.Runtime.ExceptionServices;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Represents a value read from a target's memory and compared across ticks.
/// </summary>
/// <remarks>
///     A watcher reads only when one of its values is accessed, and at most once per tick of its tick counter.
/// </remarks>
public abstract class Watcher
{
    private readonly ITickCounter _ticks;
    private readonly IMemoryReader _reader;

    private bool _isFresh;
    private long _readTick;
    private ExceptionDispatchInfo? _failure;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Watcher"/> class.
    /// </summary>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <param name="reader">The memory to read from.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/> or <paramref name="reader"/> is <see langword="null"/>.
    /// </exception>
    private protected Watcher(ITickCounter ticks, IMemoryReader reader)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentNullException.ThrowIfNull(reader);

        _ticks = ticks;
        _reader = reader;
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the watcher reads. A disabled watcher keeps its values and reports
    ///     no change.
    /// </summary>
    /// <remarks>
    ///     Enabling a watcher drops this tick's read, so the next access reads in the same tick.
    /// </remarks>
    public bool Enabled
    {
        get;
        set
        {
            if (value && !field)
            {
                Invalidate();
            }

            field = value;
        }
    } = true;

    /// <summary>
    ///     Gets or sets what happens to the values when a read fails. The default is <see cref="FailAction.Clear"/>.
    /// </summary>
    public FailAction FailAction { get; set; }

    /// <summary>
    ///     Reads the value on the first call in the current tick. Later calls in the same tick reuse that read, and a
    ///     disabled watcher skips it.
    /// </summary>
    /// <remarks>
    ///     A read that threw throws again on every later call in the same tick, because each access in 1 tick has to
    ///     get the same answer.
    /// </remarks>
    protected void EnsureFresh()
    {
        long tick = _ticks.Value;
        if (_isFresh && _readTick == tick)
        {
            _failure?.Throw();
            return;
        }

        _isFresh = true;
        _readTick = tick;
        _failure = null;

        if (!Enabled)
        {
            Skip();
            return;
        }

        try
        {
            Refresh(_reader);
        }
        catch (Exception ex)
        {
            _failure = ExceptionDispatchInfo.Capture(ex);
            throw;
        }
    }

    /// <summary>
    ///     Drops this tick's read, so the next access reads again in the same tick.
    /// </summary>
    /// <remarks>
    ///     Use it for a watcher whose read can start to work partway through a tick, such as a hook written in the
    ///     middle of one.
    /// </remarks>
    protected void Invalidate()
    {
        _isFresh = false;
        _failure = null;
    }

    private protected abstract void Refresh(IMemoryReader reader);

    private protected abstract void Skip();
}
