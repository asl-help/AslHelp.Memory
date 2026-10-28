using AslHelp.Logging;
using AslHelp.Memory.Patching.Errors;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents everything 1 run of a script has written into a target: the pools the stubs sit in and the entries
///     redirected into them.
/// </summary>
/// <remarks>
///     The session keeps the ledger, so a target never has 2 redirections of 1 method. A 2nd request returns the
///     installed patch. An entry patched by another run is adopted. Every write goes through 1 caller at a time,
///     since concurrent writes race their own protection changes and leave 1 of the game's code pages writable for the
///     rest of its life. Disposal puts every entry back before releasing anything. A pool freed while its entry still
///     jumps into it takes the target down, while 1 left behind costs only 64 KB of address space.
/// </remarks>
public sealed class PatchSession : IPatchInstaller, IDisposable
{
    private readonly object _gate = new();
    private readonly List<PatchPool> _pools = [];
    private readonly Dictionary<Address, CodePatch> _installed = [];
    private readonly List<CodePatch> _attempted = [];

    private readonly IMemoryReader _memory;
    private readonly IPatchWriter _writer;
    private readonly Logger? _logger;

    private bool _closed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PatchSession"/> class over <paramref name="memory"/>, writing
    ///     through <paramref name="writer"/>.
    /// </summary>
    /// <param name="memory">The target's memory, which the entries and the counters are read through.</param>
    /// <param name="writer">The write access to the target.</param>
    /// <param name="logger">The logger to write what the session did to, if any.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/> or <paramref name="writer"/> is <see langword="null"/>.
    /// </exception>
    internal PatchSession(
        IMemoryReader memory,
        IPatchWriter writer,
        Logger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(writer);

        _memory = memory;
        _writer = writer;
        _logger = logger;
    }

    /// <summary>
    ///     Gets the patches this session keeps, whether it installed or adopted each one.
    /// </summary>
    /// <remarks>
    ///     The collection is a copy taken under the session's lock, so an install on another thread can't change it
    ///     while it is enumerated.
    /// </remarks>
    public IReadOnlyCollection<CodePatch> Patches
    {
        get
        {
            lock (_gate)
            {
                return [.. _installed.Values];
            }
        }
    }

    /// <summary>
    ///     Redirects <paramref name="entry"/> into a stub that does nothing but count.
    /// </summary>
    /// <param name="entry">The address the method's compiled code begins at.</param>
    /// <returns>
    ///     The patch on success, otherwise a failed result with what stopped it.
    /// </returns>
    public Result<CodePatch> Install(Address entry)
    {
        return Install(entry, CaptureLayout.None);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The session puts the stub in 1 of its pools and reserves a new one where none is in reach.
    /// </remarks>
    public Result<CodePatch> Install(Address entry, CaptureLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        lock (_gate)
        {
            if (_closed)
            {
                return SessionError.Closed();
            }

            if (entry == Address.Zero)
            {
                return SessionError.NoCodeToRedirect();
            }

            if (_installed.TryGetValue(entry, out var standing))
            {
                return standing;
            }

            if (CodePatch.Adopt(_memory, entry).TryUnwrap(out var adopted))
            {
                // Takes back a redirection of this session's whose write failed after the jump reached the entry,
                // because that stub still captures with the layout it was composed for.
                if (TakeAttempted(entry, adopted.Slot) is { } own)
                {
                    _installed[entry] = own;

                    return own;
                }

                _installed[entry] = adopted;
                _logger?.LogDebug($"Adopted the patch standing at {entry}.");

                return adopted;
            }

            if (!CodePatch.Plan(_memory, entry, layout).TryUnwrap(out var plan, out var planError))
            {
                return planError;
            }

            if (!PoolFor(entry).TryUnwrap(out var pool, out var reserveError))
            {
                return reserveError;
            }

            if (!CodePatch.Install(_memory, _writer, pool, plan, out var attempted)
                .TryUnwrap(out var patch, out var installError))
            {
                // Keeps a redirection whose write failed after the target was handed it, because the jump may stand at
                // the entry all the same and disposal has to put it back before releasing the stub it points into.
                if (attempted is not null)
                {
                    _attempted.Add(attempted);
                }

                return installError;
            }

            _installed[entry] = patch;
            TakeAttempted(entry, patch.Slot);

            return patch;
        }
    }

    /// <inheritdoc/>
    public IDisposable? SuspendThreads()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return null;
            }

            if (!_writer.SuspendThreads().TryUnwrap(out var suspension, out var error))
            {
                _logger?.LogWarning(
                    $"The target's threads could not be suspended for the batch, so each write suspends them itself. {error.Message}");

                return null;
            }

            return suspension;
        }
    }

    /// <summary>
    ///     Puts every patched entry back, then releases the pools this session reserved.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;

            // Skips suspending the threads for an empty session, because a script that hooked nothing still disposes
            // its session on every reload.
            if (_installed.Count == 0 && _attempted.Count == 0 && _pools.Count == 0)
            {
                return;
            }

            // Suspends the target once for the whole unload. Every write below suspends the target itself and counts
            // its own suspensions, so a suspension that couldn't be taken only costs the stutter it would have saved.
            using (_writer.SuspendThreads().UnwrapOrDefault())
            {
                Release(Restore());
            }
        }
    }

    // Drops every attempted redirection at `entry`, because the entry now jumps into the stub at `slot` alone and
    // putting it back once covers them all. Returns the attempt whose stub is at `slot`, if there is 1.
    private CodePatch? TakeAttempted(Address entry, Address slot)
    {
        CodePatch? taken = null;

        for (int i = _attempted.Count - 1; i >= 0; i--)
        {
            var patch = _attempted[i];
            if (patch.Entry != entry)
            {
                continue;
            }

            if (patch.Slot == slot)
            {
                taken = patch;
            }

            _attempted.RemoveAt(i);
        }

        return taken;
    }

    private Result<PatchPool> PoolFor(Address entry)
    {
        foreach (var pool in _pools)
        {
            if (!pool.IsFull && pool.CanReach(entry))
            {
                return pool;
            }
        }

        if (!PatchPool.Create(_writer, entry).TryUnwrap(out var created, out var error))
        {
            return error;
        }

        _pools.Add(created);

        return created;
    }

    // Puts back every entry, adopted ones and ones whose redirection failed included, so the target is left as it was
    // found, no matter who wrote the patch. Returns the patches whose entries still jump into their stubs.
    private List<CodePatch> Restore()
    {
        List<CodePatch> unrestored = [];

        foreach (var patch in _installed.Values)
        {
            RestoreOne(patch, unrestored);
        }

        foreach (var patch in _attempted)
        {
            RestoreOne(patch, unrestored);
        }

        _installed.Clear();
        _attempted.Clear();

        return unrestored;
    }

    private void RestoreOne(CodePatch patch, List<CodePatch> unrestored)
    {
        if (patch.Restore(_memory, _writer).TryUnwrapErr(out var error))
        {
            _logger?.LogWarning($"The method at {patch.Entry} could not be put back. {error.Message}");
            unrestored.Add(patch);
        }
    }

    // Releases only what this session reserved. A pool under an adopted patch belongs to whoever wrote it and may
    // still serve a live hook. A pool under a patch that could not be put back is still where its entry jumps.
    private void Release(List<CodePatch> unrestored)
    {
        if (_pools.Count == 0)
        {
            return;
        }

        foreach (var patch in unrestored)
        {
            for (int i = 0; i < _pools.Count; i++)
            {
                var pool = _pools[i];
                if (!pool.Contains(patch.Slot))
                {
                    continue;
                }

                _logger?.LogWarning(
                    $"The pool at {pool.Base} stays in the target because the method at {patch.Entry} still jumps into it.");

                _pools.RemoveAt(i);

                break;
            }
        }

        if (_pools.Count == 0)
        {
            return;
        }

        if (!_writer.SuspendThreads().TryUnwrap(out var suspension, out var error))
        {
            _logger?.LogWarning(
                $"{_pools.Count} pool(s) stay in the target because its threads could not be suspended to check them. {error.Message}");

            _pools.Clear();

            return;
        }

        using (suspension)
        {
            foreach (var pool in _pools)
            {
                if (suspension.Any(pool.Contains))
                {
                    _logger?.LogWarning($"The pool at {pool.Base} stays in the target because a thread stands inside it.");

                    continue;
                }

                // Checks completeness after the positions, because a thread whose position couldn't be read counts as
                // missed too and a pool freed under it is a crash, not a leak.
                if (!suspension.IsComplete)
                {
                    _logger?.LogWarning(
                        $"The pool at {pool.Base} stays in the target because not every thread could be suspended and read.");

                    continue;
                }

                if (_writer.Release(pool.Base).TryUnwrapErr(out var releaseError))
                {
                    _logger?.LogWarning($"The pool at {pool.Base} could not be released. {releaseError.Message}");
                }
            }
        }

        _pools.Clear();
    }
}
