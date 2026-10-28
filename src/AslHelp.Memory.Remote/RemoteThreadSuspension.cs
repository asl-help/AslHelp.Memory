using AslHelp.Memory.Patching;
using AslHelp.Memory.Remote.Errors;
using AslHelp.Memory.Remote.Win32;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;

namespace AslHelp.Memory.Remote;

/// <summary>
///     Suspends a target's threads while its code is written and reports where each thread stands.
/// </summary>
/// <remarks>
///     A thread parked inside the bytes a patch replaces resumes into an instruction the patch removed, so a
///     patch is only safe once every thread is known to be elsewhere. Suspending is the only way to ask where a thread
///     stands and have the answer still be true when the bytes are written.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class RemoteThreadSuspension : IThreadSuspension
{
    // Answers an open of a thread id that belongs to no thread.
    private const int ErrorInvalidParameter = 87;

    // Reads the instruction pointer at a known offset in a buffer of known size, because CONTEXT is laid out per
    // architecture and CsWin32 does not generate it for an AnyCPU assembly.
    private const int Context64Size = 1232;
    private const int Context64Flags = 0x30;
    private const int Context64Rip = 0xF8;
    private const uint Control64 = 0x00100001;

    private const int Context32Size = 716;
    private const int Context32Flags = 0x00;
    private const int Context32Eip = 0xB8;
    private const uint Control32 = 0x00010001;

    private readonly List<SafeHandle> _suspended = [];
    private readonly HashSet<uint> _seen = [];
    private readonly bool _is32Bit;
    private readonly bool _wow64;

    private int _missed;

    private RemoteThreadSuspension(bool is32Bit)
    {
        _is32Bit = is32Bit;

        // Uses the WOW64 calls only from a 64-bit host, because a 32-bit host reads a 32-bit thread with the native
        // calls and a 32-bit system has no WOW64 layer to call.
        _wow64 = is32Bit && IntPtr.Size == sizeof(ulong);
    }

    /// <inheritdoc/>
    public bool IsComplete => _missed == 0;

    /// <summary>
    ///     Suspends every thread of the target, so its code can be written without one running through it.
    /// </summary>
    /// <param name="processId">The target process id.</param>
    /// <param name="is32Bit">Whether the target is a 32-bit process.</param>
    /// <returns>
    ///     The suspension on success, otherwise a failed result. Disposing it resumes what it suspended.
    /// </returns>
    public static Result<RemoteThreadSuspension> Suspend(uint processId, bool is32Bit)
    {
        if (processId == PInvoke.GetCurrentProcessId())
        {
            return RemotePatchError.CannotSuspendItself();
        }

        // Stops after 8 passes and counts a miss, because a target that keeps starting threads faster than they are
        // suspended never runs out of new ones.
        const int MaxPasses = 8;

        RemoteThreadSuspension? suspension = new(is32Bit);

        try
        {
            for (int pass = 0; ; pass++)
            {
                if (!suspension.SuspendUnseen(processId).TryUnwrap(out int found, out var error))
                {
                    return error;
                }

                if (found == 0)
                {
                    break;
                }

                if (pass == MaxPasses - 1)
                {
                    suspension._missed++;
                    break;
                }
            }

            if (suspension._seen.Count == 0)
            {
                return RemotePatchError.NoThreads();
            }

            var result = suspension;
            suspension = null;

            return result;
        }
        finally
        {
            suspension?.Dispose();
        }
    }

    // Reads the target's thread list once, suspending each thread not seen before and returning how many were new.
    private Result<int> SuspendUnseen(uint processId)
    {
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPTHREAD, 0);

        if (snapshot.IsInvalid)
        {
            return RemotePatchError.ThreadsUnlisted();
        }

        THREADENTRY32 entry = default;
        entry.dwSize = (uint)Marshal.SizeOf<THREADENTRY32>();

        if (!PInvoke.Thread32First(snapshot, ref entry))
        {
            return RemotePatchError.NoThreads();
        }

        int found = 0;

        do
        {
            if (entry.th32OwnerProcessID != processId || _seen.Contains(entry.th32ThreadID))
            {
                continue;
            }

            SafeHandle? thread = PInvoke.OpenThread_SafeHandle(
                THREAD_ACCESS_RIGHTS.THREAD_SUSPEND_RESUME | THREAD_ACCESS_RIGHTS.THREAD_GET_CONTEXT,
                false,
                entry.th32ThreadID);

            try
            {
                // Leaves the id of a thread that exited since the snapshot unseen, because the system answers that
                // id with an invalid parameter and a later thread can take the id over. Counts any other refusal as
                // missed.
                if (thread.IsInvalid)
                {
                    if (Marshal.GetLastWin32Error() != ErrorInvalidParameter)
                    {
                        _ = _seen.Add(entry.th32ThreadID);
                        found++;
                        _missed++;
                    }

                    continue;
                }

                _ = _seen.Add(entry.th32ThreadID);
                found++;

                uint previous = _wow64
                    ? PInvoke.Wow64SuspendThread(thread)
                    : PInvoke.SuspendThread(thread);

                if (previous == uint.MaxValue)
                {
                    _missed++;
                    continue;
                }

                _suspended.Add(thread);
                thread = null;
            }
            finally
            {
                thread?.Dispose();
            }
        }
        while (PInvoke.Thread32Next(snapshot, ref entry));

        return found;
    }

    /// <inheritdoc/>
    public bool AnyIn(MemoryRange range)
    {
        ulong start = range.Start.Value;
        ulong end = range.End.Value;

        return AnyAt(at => at > start && at < end);
    }

    /// <inheritdoc/>
    public bool Any(Predicate<Address> match)
    {
        return AnyAt(at => match(new Address(at)));
    }

    // Reads each suspended thread's instruction pointer and returns whether any is somewhere the predicate accepts.
    private unsafe bool AnyAt(Func<ulong, bool> inside)
    {
        int size = _is32Bit ? Context32Size : Context64Size;
        nint buffer = Marshal.AllocHGlobal(size + 16);

        try
        {
            // Aligns the buffer to 16, because the call refuses an x64 context that is not.
            nint aligned = (buffer + 15) & ~(nint)15;

            foreach (var thread in _suspended)
            {
                new Span<byte>((void*)aligned, size).Clear();

                if (_is32Bit)
                {
                    *(uint*)(aligned + Context32Flags) = Control32;

                    if (!(_wow64
                        ? NativeMethods.Wow64GetThreadContext(thread, (byte*)aligned)
                        : NativeMethods.GetThreadContext(thread, (byte*)aligned)))
                    {
                        _missed++;
                    }
                    else if (inside(*(uint*)(aligned + Context32Eip)))
                    {
                        return true;
                    }
                }
                else
                {
                    *(uint*)(aligned + Context64Flags) = Control64;

                    if (!NativeMethods.GetThreadContext(thread, (byte*)aligned))
                    {
                        _missed++;
                    }
                    else if (inside(*(ulong*)(aligned + Context64Rip)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    ///     Resumes every thread this suspension stopped.
    /// </summary>
    public void Dispose()
    {
        foreach (var thread in _suspended)
        {
            _ = PInvoke.ResumeThread(thread);
            thread.Dispose();
        }

        _suspended.Clear();
    }
}
