using System;
using System.Runtime.ExceptionServices;

namespace AslHelp.Memory.Watching;

/// <summary>
///     Represents a value that is read at most once per tick and compared across reads.
/// </summary>
/// <remarks>
///     A watcher reads only when one of its values is accessed, and at most once per tick of its tick counter.
/// </remarks>
public abstract class Watcher
{
    private ExceptionDispatchInfo? _failure;
    private bool _isFresh;
    private long _readTick;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Watcher"/> class.
    /// </summary>
    /// <param name="ticks">The tick counter that limits the watcher to 1 read per tick.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="ticks"/> is <see langword="null"/>.
    /// </exception>
    private protected Watcher(ITickCounter ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);

        Ticks = ticks;
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
        private protected set;
    }

    /// <summary>
    ///     Gets the value from the latest read as an object.
    /// </summary>
    /// <remarks>
    ///     Boxes a value type on the first access and reuses the box while the value keeps the same bits, so an unchanged
    ///     value doesn't allocate on each tick.
    /// </remarks>
    public object? Current
    {
        get
        {
            EnsureFresh();
            return BoxCurrent();
        }
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
    ///     Gets the error of the latest read, or <see langword="null"/> when the read succeeded.
    /// </summary>
    /// <remarks>
    ///     Doesn't read, so a check can look at a watcher without moving its values. A read that threw sets it to an
    ///     <see cref="ExceptionError"/> with the exception. A disabled watcher keeps the error of its latest read.
    /// </remarks>
    public ResultError? Error { get; private protected set; }

    /// <summary>
    ///     Gets or sets what happens to the values when a read fails. The default is <see cref="FailAction.Clear"/>.
    /// </summary>
    /// <remarks>
    ///     A read that throws fails too. Under <see cref="FailAction.Throw"/> its own exception comes back out.
    /// </remarks>
    public FailAction FailAction { get; set; }

    /// <summary>
    ///     Gets the value from the read before the latest one as an object, however many ticks ago it was.
    /// </summary>
    /// <remarks>
    ///     Is the same object <see cref="Current"/> returned before the change.
    /// </remarks>
    public object? Old
    {
        get
        {
            EnsureFresh();
            return BoxOld();
        }
    }

    /// <summary>
    ///     Gets the tick counter that limits the watcher to 1 read per tick.
    /// </summary>
    protected ITickCounter Ticks { get; }

    /// <summary>
    ///     Reads the value on the first call in the current tick. Later calls in the same tick reuse that read, and a
    ///     disabled watcher skips it.
    /// </summary>
    /// <remarks>
    ///     A read that threw throws again on every later call in the same tick while the watcher is enabled, because
    ///     each access in 1 tick has to get the same answer.
    /// </remarks>
    protected void EnsureFresh()
    {
        long tick = Ticks.Value;
        if (_isFresh && _readTick == tick)
        {
            // Skip the failure of a read before Enabled turned false, because a disabled watcher skips the read.
            if (Enabled)
            {
                _failure?.Throw();
            }

            return;
        }

        _isFresh = true;
        _readTick = tick;
        _failure = null;

        if (!Enabled)
        {
            Changed = false;
            return;
        }

        try
        {
            Refresh();
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

    private protected abstract object? BoxCurrent();

    private protected abstract object? BoxOld();

    private protected abstract void Refresh();
}
