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

    private long _readTick = -1;
    private ExceptionDispatchInfo? _failure;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Watcher"/> class.
    /// </summary>
    /// <param name="ticks">The tick counter the watcher reads at most once per tick of.</param>
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
    ///     Gets or sets a value indicating whether the watcher reads. A disabled watcher keeps its values.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Gets or sets what happens to the values when a read fails.
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
        if (_readTick == tick)
        {
            _failure?.Throw();
            return;
        }

        _readTick = tick;
        _failure = null;

        if (!Enabled)
        {
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
        _readTick = -1;
        _failure = null;
    }

    private protected abstract void Refresh(IMemoryReader reader);
}
