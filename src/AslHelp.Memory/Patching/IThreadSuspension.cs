using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents a suspension of every thread of a target while its code is written.
/// </summary>
/// <remarks>
///     A thread parked inside the bytes a patch replaces would resume into an instruction no longer there, so a patch
///     is only safe once every thread is known to be elsewhere. Suspending is the one way to ask where a thread stands
///     and have the answer still be true when the bytes are written. Disposing the suspension resumes every thread
///     it suspended.
/// </remarks>
public interface IThreadSuspension : IDisposable
{
    /// <summary>
    ///     Gets a value indicating whether every thread of the target is suspended and can be asked where it stands.
    /// </summary>
    /// <remarks>
    ///     A thread that could not be opened, suspended or read gives no answer about where it stands, so this reports
    ///     false. So does a target that kept starting threads faster than they could be listed. A caller must not treat
    ///     a missing answer as safe.
    /// </remarks>
    bool IsComplete { get; }

    /// <summary>
    ///     Determines whether any suspended thread stands inside <paramref name="range"/>, the first byte excluded.
    /// </summary>
    /// <remarks>
    ///     A thread stopped exactly at the entry is the ordinary case and is safe: it has not read the bytes yet and
    ///     will read what replaces them.
    /// </remarks>
    /// <param name="range">The bytes the patch overwrites.</param>
    /// <returns>
    ///     Whether a thread stands within the range.
    /// </returns>
    bool AnyIn(MemoryRange range);

    /// <summary>
    ///     Determines whether any suspended thread stands at an address <paramref name="match"/> accepts.
    /// </summary>
    /// <remarks>
    ///     A page about to be released asks this. There the first byte counts as much as the rest.
    /// </remarks>
    /// <param name="match">Determines whether an instruction pointer is somewhere the caller cares about.</param>
    /// <returns>
    ///     Whether a thread stands somewhere it accepts.
    /// </returns>
    bool Any(Predicate<Address> match);
}
