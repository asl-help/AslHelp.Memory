using AslHelp.Memory.Remote.Errors;
using AslHelp.Memory.Remote.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
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
    private const PROCESS_ACCESS_RIGHTS Access =
        PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION
        | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ
        | PROCESS_ACCESS_RIGHTS.PROCESS_VM_WRITE
        | PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION;

    // The longest path the wide Windows APIs accept, in characters.
    private const int MaxPathLength = 32767;

    private readonly SafeFileHandle _handle;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RemoteProcessMemory"/> class, opening
    ///     <paramref name="process"/> for reads and writes.
    /// </summary>
    /// <param name="process">The process to open.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="process"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="Win32Exception">
    ///     The process couldn't be opened or its bitness couldn't be found.
    /// </exception>
    public RemoteProcessMemory(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        int id = process.Id;

        _handle = PInvoke.OpenProcess_SafeHandle(Access, false, (uint)id);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Couldn't open process {id} for memory access.");
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

            throw new Win32Exception(error, $"Couldn't find whether process {id} runs under WOW64.");
        }

        Layout = isWow64 ? MemoryLayout.X86 : MemoryLayout.X64;
    }

    /// <inheritdoc/>
    public MemoryLayout Layout { get; }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">
    ///     The instance is disposed.
    /// </exception>
    public unsafe Result Read(Address address, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        if (!TryGetHostPointer(address, out void* pointer))
        {
            return RemoteMemoryError.Unreadable(address, buffer.Length);
        }

        if (buffer.IsEmpty)
        {
            return Result.Ok();
        }

        bool success = PInvoke.ReadProcessMemory(_handle, pointer, buffer, out nuint read);

        return success && read == (nuint)buffer.Length
            ? Result.Ok()
            : RemoteMemoryError.Unreadable(address, buffer.Length);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">
    ///     The instance is disposed.
    /// </exception>
    public unsafe Result Write(Address address, ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        if (!TryGetHostPointer(address, out void* pointer))
        {
            return RemoteMemoryError.Unwritable(address, buffer.Length);
        }

        if (buffer.IsEmpty)
        {
            return Result.Ok();
        }

        bool success = PInvoke.WriteProcessMemory(_handle, pointer, buffer, out nuint written);

        return success && written == (nuint)buffer.Length
            ? Result.Ok()
            : RemoteMemoryError.Unwritable(address, buffer.Length);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A region with <c>PAGE_GUARD</c> or <c>PAGE_NOACCESS</c> reports <see cref="MemoryProtection.None"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    ///     The instance is disposed.
    /// </exception>
    public unsafe Result<IReadOnlyList<MemoryRegion>> GetRegions(MemoryRange range)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        List<MemoryRegion> regions = [];
        var address = range.Start;
        var end = range.End;

        while (address < end && TryGetHostPointer(address, out void* pointer))
        {
            if (NativeMethods.VirtualQueryEx(_handle, pointer, out var info, (nuint)sizeof(MemoryBasicInformation)) == 0)
            {
                break;
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
    /// <exception cref="ObjectDisposedException">
    ///     The instance is disposed.
    /// </exception>
    public Result<IReadOnlyList<MemoryModule>> GetModules()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        // Keeps the handle open for the raw calls, which take a HANDLE where Dispose can't reach it.
        bool added = false;

        try
        {
            _handle.DangerousAddRef(ref added);

            return GetModules((HANDLE)_handle.DangerousGetHandle());
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
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
            MODULEINFO info;
            if (!PInvoke.GetModuleInformation(process, handles[i], &info, (uint)sizeof(MODULEINFO)))
            {
                continue;
            }

            string? name = GetBaseName(process, handles[i], text);
            if (name is null)
            {
                continue;
            }

            string? path = GetFileName(process, handles[i], text);
            MemoryRange range = new(new((ulong)info.lpBaseOfDll), info.SizeOfImage);

            modules.Add(new(name, path, range));
        }

        return modules;
    }

    /// <summary>
    ///     Closes the handle to the process.
    /// </summary>
    public void Dispose()
    {
        _handle.Dispose();
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
