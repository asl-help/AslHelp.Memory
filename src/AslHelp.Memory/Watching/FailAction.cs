namespace AslHelp.Memory.Watching;

/// <summary>
///     Specifies what happens to a watcher's values when a read fails.
/// </summary>
public enum FailAction
{
    /// <summary>
    ///     Resets both values to their defaults, so the next successful read fills them and doesn't count as a change.
    /// </summary>
    Clear,

    /// <summary>
    ///     Keeps the values from the last successful read.
    /// </summary>
    KeepOld,

    /// <summary>
    ///     Throws an <see cref="System.InvalidOperationException"/> with the error of the read.
    /// </summary>
    /// <remarks>
    ///     Use it for a value that is always there once the game runs, because then a failure means the script reads
    ///     the wrong place. Use <see cref="Clear"/> or <see cref="KeepOld"/> for a value that may not be there yet.
    /// </remarks>
    Throw,
}
