using AslHelp.Memory.Remote.Errors;
using AslHelp.Memory.Remote.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Memory;
using Windows.Win32.System.ProcessStatus;
using Windows.Win32.System.Threading;

namespace AslHelp.Memory.Remote;

/// <summary>
///     Provides reads, writes, regions and modules of another running Windows process.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RemoteProcessMemory : IMemoryReader, IMemoryWriter, IMemoryRegionProvider, IMemoryModuleProvider, IDisposable
{
    private const PROCESS_ACCESS_RIGHTS ReadAccess = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ;
    private const PROCESS_ACCESS_RIGHTS WriteAccess =
        PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION
        | PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION
        | PROCESS_ACCESS_RIGHTS.PROCESS_VM_WRITE;

    // The longest path the wide Windows APIs accept, in characters.
    private const int MaxPathLength = 32767;

    private readonly SafeFileHandle _handle;

    // Opens on the 1st write, because a process that refuses the rights to write can still be read.
    private SafeFileHandle? _writeHandle;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RemoteProcessMemory"/> class, opening
    ///     <paramref name="process"/> for reads.
    /// </summary>
    /// <param name="process">The process to open.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="process"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    ///     The 1st write opens a 2nd handle with the rights to write, so a process that refuses them can still be read.
    /// </remarks>
    /// <exception cref="Win32Exception">
    ///     The process couldn't be opened for reads or its bitness couldn't be found.
    /// </exception>
    public RemoteProcessMemory(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        int id = process.Id;
        ProcessId = (uint)id;

        _handle = PInvoke.OpenProcess_SafeHandle(ReadAccess, false, (uint)id);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), SR.FormatProcessNotOpened(id));
        }

        if (!Environment.Is64BitOperatingSystem)
        {
            Layout = MemoryLayout.X86;
            return;
        }

        if (!PInvoke.IsWow64Process(_handle, out var isWow64))
        {
            int error = Marshal.GetLastWin32Error();
            _handle.Dispose();

            throw new Win32Exception(error, SR.FormatWow64NotFound(id));
        }

        Layout = isWow64 ? MemoryLayout.X86 : MemoryLayout.X64;
    }

    /// <inheritdoc/>
    public MemoryLayout Layout { get; }

    /// <summary>
    ///     Gets the id of the process.
    /// </summary>
    internal uint ProcessId { get; }

    /// <inheritdoc/>
    public unsafe Result Read(Address address, Span<byte> buffer)
    {
        if (_handle.IsClosed)
        {
            return RemoteMemoryError.ProcessClosed();
        }

        if (!TryGetHostPointer(address, out void* pointer))
        {
            return new RemoteUnreadableError(address, buffer.Length);
        }

        if (buffer.IsEmpty)
        {
            return Result.Ok();
        }

        try
        {
            bool success = PInvoke.ReadProcessMemory(_handle, pointer, buffer, out nuint read);

            return success && read == (nuint)buffer.Length
                ? Result.Ok()
                : new RemoteUnreadableError(address, buffer.Length);
        }
        catch (ObjectDisposedException)
        {
            // Fail like a closed handle, because Dispose on another thread can close it after the check.
            return RemoteMemoryError.ProcessClosed();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A write that Windows refuses makes each page writable for the write and puts its protection back after it. A
    ///     code page keeps its execute right during the write.
    /// </remarks>
    public unsafe Result Write(Address address, ReadOnlySpan<byte> buffer)
    {
        if (_handle.IsClosed)
        {
            return RemoteMemoryError.ProcessClosed();
        }

        if (!TryGetHostPointer(address, out void* pointer))
        {
            return RemoteMemoryError.Unwritable(address, buffer.Length);
        }

        if (buffer.IsEmpty)
        {
            return Result.Ok();
        }

        if (!TryOpenForWrites(out var writeHandle, out int openError))
        {
            return RemoteMemoryError.WriteHandleRefused(new Win32Exception(openError).Message);
        }

        try
        {
            // Try the write as it is first, because a writable page needs no protection change and code a thread runs
            // keeps its rights.
            if (PInvoke.WriteProcessMemory(writeHandle, pointer, buffer, out nuint written))
            {
                return written == (nuint)buffer.Length ? Result.Ok() : RemoteMemoryError.Unwritable(address, buffer.Length);
            }

            if (!PageProtection.TryMakeWritable(writeHandle, (nuint)pointer, buffer.Length, out var protection, out _))
            {
                return RemoteMemoryError.Unwritable(address, buffer.Length);
            }

            bool success = PInvoke.WriteProcessMemory(writeHandle, pointer, buffer, out written);
            _ = protection.TryRestore(out _, out _);

            return success && written == (nuint)buffer.Length
                ? Result.Ok()
                : RemoteMemoryError.Unwritable(address, buffer.Length);
        }
        catch (ObjectDisposedException)
        {
            // Fail like a closed handle, because Dispose on another thread can close it after the check.
            return RemoteMemoryError.ProcessClosed();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A region with <c>PAGE_GUARD</c> or <c>PAGE_NOACCESS</c> reports <see cref="MemoryProtection.None"/>.
    /// </remarks>
    public Result<IReadOnlyList<MemoryRegion>> GetRegions(MemoryRange range)
    {
        if (_handle.IsClosed)
        {
            return RemoteMemoryError.ProcessClosed();
        }

        try
        {
            return ListRegions(range);
        }
        catch (ObjectDisposedException)
        {
            // Fail like a closed handle, because Dispose on another thread can close it after the check.
            return RemoteMemoryError.ProcessClosed();
        }
    }

    private unsafe Result<IReadOnlyList<MemoryRegion>> ListRegions(MemoryRange range)
    {
        // Matches ERROR_INVALID_PARAMETER, which Windows returns for an address above the top of user memory.
        const int InvalidParameter = 87;

        List<MemoryRegion> regions = [];
        var address = range.Start;

        // Checks with Contains, because End wraps to 0 for a range at the top of memory.
        while (range.Contains(address) && TryGetHostPointer(address, out void* pointer))
        {
            if (NativeMethods.VirtualQueryEx(_handle, pointer, out var info, (nuint)sizeof(MemoryBasicInformation)) == 0)
            {
                int error = Marshal.GetLastWin32Error();

                // End the list at the top of user memory and fail on any other refusal, because a short list reads as a
                // scan that found nothing.
                if (error == InvalidParameter)
                {
                    break;
                }

                return RemoteMemoryError.RegionsUnavailable(address, new Win32Exception(error).Message);
            }

            Address start = new(info.BaseAddress);
            ulong size = info.RegionSize;

            if (info.State == VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT)
            {
                regions.Add(new(new(start, size), ToProtection(info.Protect)));
            }

            var next = start + (long)size;
            if (next <= address)
            {
                break;
            }

            address = next;
        }

        return regions;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The modules come from a Toolhelp snapshot, which gives a 32-bit process the SysWOW64 paths of its system
    ///     modules. When Windows refuses the snapshot, the modules come from the process handle, which gives those modules
    ///     their System32 paths.
    /// </remarks>
    public Result<IReadOnlyList<MemoryModule>> GetModules()
    {
        if (_handle.IsClosed)
        {
            return RemoteMemoryError.ProcessClosed();
        }

        if (GetSnapshotModules() is { } snapshotModules)
        {
            return snapshotModules;
        }

        // Keeps the handle open for the raw calls, which take a HANDLE where Dispose can't reach it.
        bool added = false;

        try
        {
            _handle.DangerousAddRef(ref added);

            return GetModules((HANDLE)_handle.DangerousGetHandle());
        }
        catch (ObjectDisposedException)
        {
            // Fail like a closed handle, because Dispose on another thread can close it after the check.
            return RemoteMemoryError.ProcessClosed();
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

    private unsafe List<MemoryModule>? GetSnapshotModules()
    {
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(
            CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPMODULE | CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPMODULE32,
            ProcessId);

        MODULEENTRY32W entry = new() { dwSize = (uint)sizeof(MODULEENTRY32W) };

        // Leave a refused snapshot to the process handle, because Windows refuses it while a process starts.
        if (snapshot.IsInvalid || !PInvoke.Module32FirstW(snapshot, ref entry))
        {
            return null;
        }

        List<MemoryModule> modules = [];

        do
        {
            MemoryRange range = new(new((ulong)entry.modBaseAddr), entry.modBaseSize);
            modules.Add(new(entry.szModule.ToString(), entry.szExePath.ToString(), range));
        } while (PInvoke.Module32NextW(snapshot, ref entry));

        return modules;
    }

    private static unsafe Result<IReadOnlyList<MemoryModule>> GetModules(HANDLE process)
    {
        HMODULE[] handles = [];
        uint needed = 0;

        do
        {
            if (needed > 0)
            {
                handles = new HMODULE[needed / sizeof(HMODULE)];
            }

            fixed (HMODULE* first = handles)
            {
                if (!PInvoke.EnumProcessModulesEx(
                    process,
                    first,
                    (uint)(handles.Length * sizeof(HMODULE)),
                    &needed,
                    ENUM_PROCESS_MODULES_EX_FLAGS.LIST_MODULES_ALL))
                {
                    return RemoteMemoryError.ModulesUnavailable(new Win32Exception(Marshal.GetLastWin32Error()).Message);
                }
            }
        } while (needed > handles.Length * sizeof(HMODULE));

        int count = (int)(needed / sizeof(HMODULE));
        List<MemoryModule> modules = [];
        char[] text = new char[MaxPathLength];

        for (int i = 0; i < count; i++)
        {
            if (Describe(process, handles[i], text) is { } module)
            {
                modules.Add(module);
            }
        }

        return modules;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Windows lists the executable of a process 1st, so this is the 1st module it lists, as in
    ///     <see cref="Process.MainModule"/>.
    /// </remarks>
    public Result<MemoryModule> GetMainModule()
    {
        if (_handle.IsClosed)
        {
            return RemoteMemoryError.ProcessClosed();
        }

        // Keeps the handle open for the raw calls, which take a HANDLE where Dispose can't reach it.
        bool added = false;

        try
        {
            _handle.DangerousAddRef(ref added);

            return GetMainModule((HANDLE)_handle.DangerousGetHandle());
        }
        catch (ObjectDisposedException)
        {
            // Fail like a closed handle, because Dispose on another thread can close it after the check.
            return RemoteMemoryError.ProcessClosed();
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

    private static unsafe Result<MemoryModule> GetMainModule(HANDLE process)
    {
        // Requests only 1 handle, so a later module is never taken when the 1st one can't be described.
        HMODULE first = default;
        uint needed;

        if (!PInvoke.EnumProcessModulesEx(process, &first, (uint)sizeof(HMODULE), &needed, ENUM_PROCESS_MODULES_EX_FLAGS.LIST_MODULES_ALL))
        {
            return RemoteMemoryError.ModulesUnavailable(new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        if (needed == 0 || Describe(process, first, new char[MaxPathLength]) is not { } module)
        {
            return RemoteMemoryError.MainModuleNotListed();
        }

        return module;
    }

    /// <summary>
    ///     Closes the handles to the process.
    /// </summary>
    public void Dispose()
    {
        _handle.Dispose();
        Interlocked.Exchange(ref _writeHandle, null)?.Dispose();
    }

    private bool TryOpenForWrites(out SafeFileHandle writeHandle, out int win32Error)
    {
        win32Error = 0;

        if (Volatile.Read(ref _writeHandle) is { } open)
        {
            writeHandle = open;
            return true;
        }

        var opened = PInvoke.OpenProcess_SafeHandle(WriteAccess, false, ProcessId);
        if (opened.IsInvalid)
        {
            win32Error = Marshal.GetLastWin32Error();
            opened.Dispose();

            writeHandle = null!;
            return false;
        }

        // Keep the handle of the 1st thread, because 2 threads can write at once and only 1 handle gets disposed.
        if (Interlocked.CompareExchange(ref _writeHandle, opened, null) is { } other)
        {
            opened.Dispose();
            opened = other;
        }

        writeHandle = opened;
        return true;
    }

    private static unsafe MemoryModule? Describe(HANDLE process, HMODULE module, char[] text)
    {
        MODULEINFO info;
        if (!PInvoke.GetModuleInformation(process, module, &info, (uint)sizeof(MODULEINFO)))
        {
            return null;
        }

        string? path = GetFileName(process, module, text);

        // Take the name from the path, because Windows can refuse the base name of a module that is still loading.
        string? name = GetBaseName(process, module, text) ?? System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        MemoryRange range = new(new((ulong)info.lpBaseOfDll), info.SizeOfImage);

        return new(name, path, range);
    }

    private static unsafe string? GetBaseName(HANDLE process, HMODULE module, char[] text)
    {
        fixed (char* first = text)
        {
            uint length = PInvoke.GetModuleBaseName(process, module, first, (uint)text.Length);
            return length == 0 ? null : new string(first, 0, (int)length);
        }
    }

    private static unsafe string? GetFileName(HANDLE process, HMODULE module, char[] text)
    {
        fixed (char* first = text)
        {
            uint length = PInvoke.GetModuleFileNameEx(process, module, first, (uint)text.Length);
            return length == 0 ? null : new string(first, 0, (int)length);
        }
    }

    private static unsafe bool TryGetHostPointer(Address address, out void* pointer)
    {
        pointer = null;
        if (IntPtr.Size == sizeof(uint) && address.Value > uint.MaxValue)
        {
            return false;
        }

        pointer = (void*)(nuint)address.Value;
        return true;
    }

    private static MemoryProtection ToProtection(PAGE_PROTECTION_FLAGS flags)
    {
        const MemoryProtection ReadWrite = MemoryProtection.Read | MemoryProtection.Write;
        const MemoryProtection ReadExecute = MemoryProtection.Read | MemoryProtection.Execute;
        const MemoryProtection All = ReadWrite | MemoryProtection.Execute;

        if ((flags & PAGE_PROTECTION_FLAGS.PAGE_GUARD) != 0)
        {
            return MemoryProtection.None;
        }

        // Masks off PAGE_NOCACHE, PAGE_WRITECOMBINE and the other modifiers above the low byte.
        return (flags & (PAGE_PROTECTION_FLAGS)0xFF) switch
        {
            PAGE_PROTECTION_FLAGS.PAGE_READONLY => MemoryProtection.Read,
            PAGE_PROTECTION_FLAGS.PAGE_READWRITE or PAGE_PROTECTION_FLAGS.PAGE_WRITECOPY => ReadWrite,
            PAGE_PROTECTION_FLAGS.PAGE_EXECUTE => MemoryProtection.Execute,
            PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READ => ReadExecute,
            PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_READWRITE or PAGE_PROTECTION_FLAGS.PAGE_EXECUTE_WRITECOPY => All,
            _ => MemoryProtection.None,
        };
    }
}
