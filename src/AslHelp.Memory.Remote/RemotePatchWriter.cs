using AslHelp.Logging;
using AslHelp.Memory.Patching;
using AslHelp.Memory.Remote.Errors;
using AslHelp.Memory.Remote.Win32;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Threading;

namespace AslHelp.Memory.Remote;

/// <summary>
///     Puts code into a target process: pages the target may execute and the bytes that reach them.
/// </summary>
/// <remarks>
///     Writing data is <see cref="IMemoryWriter.Write"/>. Forcing a page executable and flushing the instruction cache
///     is a different operation, which is why they are apart. A reservation has to be within a relative jump of its
///     anchor, which is why this allocates rather than taking any page the target has free. The patcher opens a
///     separate handle, so a target that refuses the wider rights can still be read. Once the writer is disposed, every
///     member returns a failed result rather than throwing, so a session can still put its entries back in any order.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe class RemotePatchWriter : IPatchWriter, IDisposable
{
    private readonly SafeHandle _handle;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RemotePatchWriter"/> class, opening the process behind
    ///     <paramref name="memory"/> for writing.
    /// </summary>
    /// <param name="memory">The memory of the process to write into, which also gives its layout.</param>
    /// <param name="log">Where patches record what they did. Omitting it records nowhere.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="Win32Exception">
    ///     The process could not be opened for writing.
    /// </exception>
    public RemotePatchWriter(
        RemoteProcessMemory memory,
        Logger? log = null)
    {
        ArgumentNullException.ThrowIfNull(memory);

        Log = log ?? new Logger();
        ProcessId = memory.ProcessId;
        Is32Bit = memory.Layout.PointerSize == 4;

        // Asks for only the rights the calls below need, because an unused right is one more way to be refused a
        // target that would otherwise open.
        _handle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION
            | PROCESS_ACCESS_RIGHTS.PROCESS_VM_WRITE
            | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ,
            false,
            ProcessId);

        if (_handle.IsInvalid)
        {
            throw new Win32Exception();
        }
    }

    /// <summary>
    ///     Gets the target process id.
    /// </summary>
    internal uint ProcessId { get; }

    /// <summary>
    ///     Gets a value indicating whether the target is a 32-bit process.
    /// </summary>
    internal bool Is32Bit { get; }

    /// <summary>
    ///     Gets where patches record what they did.
    /// </summary>
    internal Logger Log { get; }

    /// <inheritdoc/>
    public Result<Address> Reserve(Address anchor, int size)
    {
        if (!TryAcquire())
        {
            return RemotePatchError.HandleReleased();
        }

        try
        {
            return ReserveNear(anchor, size);
        }
        finally
        {
            _handle.DangerousRelease();
        }
    }

    /// <inheritdoc/>
    public Result Protect(Address address, int size, MemoryProtection protection)
    {
        if (!TryGetHostPointer(address, out nuint at))
        {
            return RemotePatchError.Unaddressable(address);
        }

        if (!TryAcquire())
        {
            return RemotePatchError.HandleReleased();
        }

        try
        {
            if (!PInvoke.VirtualProtectEx(_handle, (void*)at, (nuint)size, ToPageProtection(protection), out _))
            {
                return RemotePatchError.NotProtected(address, new Win32Exception().Message);
            }

            return Result.Ok();
        }
        finally
        {
            _handle.DangerousRelease();
        }
    }

    /// <inheritdoc/>
    public Result WriteCode(Address address, ReadOnlySpan<byte> buffer)
    {
        if (!TryGetHostPointer(address, out nuint at))
        {
            return RemotePatchError.Unaddressable(address);
        }

        if (!TryAcquire())
        {
            return RemotePatchError.HandleReleased();
        }

        try
        {
            return buffer.IsEmpty ? Result.Ok() : WriteCodeAt(address, at, buffer);
        }
        finally
        {
            _handle.DangerousRelease();
        }
    }

    /// <inheritdoc/>
    public Result Release(Address reservation)
    {
        if (!TryGetHostPointer(reservation, out nuint at))
        {
            return RemotePatchError.Unaddressable(reservation);
        }

        if (!TryAcquire())
        {
            return RemotePatchError.HandleReleased();
        }

        try
        {
            if (!PInvoke.VirtualFreeEx(_handle, (void*)at, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE))
            {
                return RemotePatchError.NotReleased(reservation, new Win32Exception().Message);
            }

            return Result.Ok();
        }
        finally
        {
            _handle.DangerousRelease();
        }
    }

    /// <inheritdoc/>
    public Result<IThreadSuspension> SuspendThreads()
    {
        if (_handle.IsClosed)
        {
            return RemotePatchError.HandleReleased();
        }

        if (!RemoteThreadSuspension.Suspend(ProcessId, Is32Bit).TryUnwrap(out var suspension, out var error))
        {
            return error;
        }

        return suspension;
    }

    /// <summary>
    ///     Releases the patch handle. Nothing is put back here, which is the session's job.
    /// </summary>
    public void Dispose()
    {
        _handle.Dispose();
    }

    // Takes a reference on the handle for the whole call, so a Dispose on another thread waits for the call to end
    // rather than closing the handle under it.
    private bool TryAcquire()
    {
        bool acquired = false;

        try
        {
            _handle.DangerousAddRef(ref acquired);
        }
        catch (ObjectDisposedException) { }

        return acquired;
    }

    // Searches outward from the anchor for the nearest free block a relative jump reaches and allocates it there.
    private Result<Address> ReserveNear(Address anchor, int size)
    {
        const long PageSize = 0x1000;
        const long Granularity = 0x10000;
        const int ErrorInvalidParameter = 87;

        // Stops 1 block short of int.MaxValue so the arithmetic below cannot overflow.
        const long Rel32Reach = int.MaxValue - Granularity;

        // Caps the top at 4 GB for a 32-bit target or host, because neither can point past it.
        long top = Is32Bit || IntPtr.Size == sizeof(uint) ? uint.MaxValue - PageSize : long.MaxValue - Rel32Reach;

        if (anchor.Value > (ulong)top)
        {
            return RemotePatchError.OutOfReach(anchor);
        }

        long origin = (long)anchor.Value & ~(Granularity - 1);
        long lowest = Math.Max(Granularity, origin - Rel32Reach);
        long highest = Math.Min(top, origin + Rel32Reach);

        long below = origin - Granularity;
        long above = origin + Granularity;

        while (below >= lowest || above <= highest)
        {
            bool up = above <= highest && (below < lowest || above - origin <= origin - below);
            long candidate = up ? above : below;

            if (NativeMethods.VirtualQueryEx(_handle, (void*)(nuint)candidate, out var region, (nuint)sizeof(MemoryBasicInformation)) == 0)
            {
                // Ends the upward side at the top of the address space, where every query is refused alike.
                bool past = up && Marshal.GetLastWin32Error() == ErrorInvalidParameter;

                above = up ? (past ? highest + Granularity : above + Granularity) : above;
                below -= up ? 0 : Granularity;

                continue;
            }

            long start = (long)region.BaseAddress;
            long end = start + (long)region.RegionSize;

            // Takes the candidate going up. Going down, takes the highest aligned address the free region fits the
            // reservation at, which is the nearest one to the anchor.
            long at = up ? candidate : Math.Min(candidate, end - size) & ~(Granularity - 1);

            if (region.State == VIRTUAL_ALLOCATION_TYPE.MEM_FREE
                && at >= Math.Max(start, lowest)
                && at + size <= end
                && Allocate((nuint)at, size) is not 0 and var page)
            {
                Log.LogDebug($"patch reservation 0x{(ulong)page:X} for 0x{(ulong)anchor:X}");

                return new Address(page);
            }

            // Moves past the whole region, used or too small, rather than asking about each block inside it.
            above = up ? Math.Max(above + Granularity, (end + Granularity - 1) & ~(Granularity - 1)) : above;
            below = up ? below : Math.Min(below - Granularity, (start - 1) & ~(Granularity - 1));
        }

        return RemotePatchError.OutOfReach(anchor);
    }

    // Makes each page the buffer touches executable and writable, writes the buffer and puts each page back.
    private Result WriteCodeAt(Address address, nuint at, ReadOnlySpan<byte> buffer)
    {
        if (!PageProtection.TryChange(
            _handle,
            at,
            buffer.Length,
            PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE,
            out var protection,
            out int protectError))
        {
            return RemotePatchError.NotMadeExecutable(address, new Win32Exception(protectError).Message);
        }

        bool wrote = PInvoke.WriteProcessMemory(_handle, (void*)at, buffer, out nuint written);

        // Takes the write error before putting the protection back, which overwrites it.
        int writeError = wrote ? 0 : Marshal.GetLastWin32Error();

        // Puts the protection back before checking the write, so a code page is never left writable.
        if (!protection.TryRestore(out nuint refusedAt, out int restoreError))
        {
            Log.LogWarning(
                $"The page at 0x{(ulong)refusedAt:X} stays executable and writable: {new Win32Exception(restoreError).Message}");
        }

        if (!wrote)
        {
            return RemotePatchError.CodeNotWritten(address, new Win32Exception(writeError).Message);
        }

        if (written != (nuint)buffer.Length)
        {
            return RemotePatchError.CodePartlyWritten(address, buffer.Length, written);
        }

        _ = PInvoke.FlushInstructionCache((HANDLE)_handle.DangerousGetHandle(), (void*)at, (nuint)buffer.Length);

        return Result.Ok();
    }

    // Allocates a block the target may read and run, at exactly the address given or not at all.
    private nuint Allocate(nuint address, int size)
    {
        return (nuint)PInvoke.VirtualAllocEx(
            _handle,
            (void*)address,
            (nuint)size,
            VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
            PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READ);
    }

    // Refuses an address above what a 32-bit host can point at, rather than cutting it down to one it can.
    private static bool TryGetHostPointer(Address address, out nuint pointer)
    {
        if (IntPtr.Size == sizeof(uint) && address.Value > uint.MaxValue)
        {
            pointer = 0;
            return false;
        }

        pointer = (nuint)address.Value;
        return true;
    }

    // Maps the access a caller asks for to the page protection the system uses for it.
    private static PAGE_PROTECTION_FLAGS ToPageProtection(MemoryProtection protection)
    {
        return protection switch
        {
            MemoryProtection.None => PAGE_PROTECTION_FLAGS.PAGE_NOACCESS,
            MemoryProtection.Read => PAGE_PROTECTION_FLAGS.PAGE_READONLY,
            MemoryProtection.Read | MemoryProtection.Write => PAGE_PROTECTION_FLAGS.PAGE_READWRITE,
            MemoryProtection.Execute => PAGE_PROTECTION_FLAGS.PAGE_EXECUTE,
            MemoryProtection.Read | MemoryProtection.Execute => PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READ,
            MemoryProtection.Read | MemoryProtection.Write | MemoryProtection.Execute => PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE,
            MemoryProtection.Write => PAGE_PROTECTION_FLAGS.PAGE_READWRITE,
            MemoryProtection.Write | MemoryProtection.Execute => PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE,
            _ => PAGE_PROTECTION_FLAGS.PAGE_NOACCESS,
        };
    }
}
