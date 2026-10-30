using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents what writes stubs into a target on a watcher's behalf.
/// </summary>
/// <remarks>
///     The watchers only ever ask for 2 things: a stub at an entry and the target kept still while several of them
///     ask at once. Anything that provides both can stand in for the live session, including a recording installer in
///     a test.
/// </remarks>
public interface IPatchInstaller
{
    /// <summary>
    ///     Redirects <paramref name="entry"/> into a stub that counts and copies out whatever
    ///     <paramref name="layout"/> lists.
    /// </summary>
    /// <remarks>
    ///     A patch already standing at the entry is adopted, layout and all, since the target is running correctly with
    ///     it. What it captures is its own writer's choice, so a caller that needs a payload checks
    ///     <see cref="CodePatch.HasCaptures"/>.
    /// </remarks>
    /// <param name="entry">The address the method's compiled code begins at.</param>
    /// <param name="layout">What the stub copies out of the call, which may be nothing.</param>
    /// <returns>
    ///     The patch on success, otherwise a failed result with what stopped it.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="layout"/> is <see langword="null"/>.
    /// </exception>
    Result<CodePatch> Install(Address entry, CaptureLayout layout);

    /// <summary>
    ///     Keeps every thread of the target still until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    ///     A write suspends the target itself either way and counts its own suspensions, so a write inside this
    ///     suspension leaves the target suspended when the write finishes. A batch therefore costs 1 stop of the game
    ///     rather than 1 per write. No write has to trust that somebody else stopped it.
    /// </remarks>
    /// <returns>
    ///     The handle when the target's threads were stopped, otherwise <see langword="null"/>. Each write then stops
    ///     the target for itself as it always does.
    /// </returns>
    IDisposable? SuspendThreads();
}
