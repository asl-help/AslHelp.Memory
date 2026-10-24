namespace AslHelp.Memory.Watching;

/// <summary>
///     Provides the number of ticks that have passed, so a watcher reads at most once per tick.
/// </summary>
public interface ITickCounter
{
    /// <summary>
    ///     Gets the number of ticks that have passed.
    /// </summary>
    long Value { get; }
}
