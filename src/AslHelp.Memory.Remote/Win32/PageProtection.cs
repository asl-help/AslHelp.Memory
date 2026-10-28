using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Windows.Win32;
using Windows.Win32.System.Memory;

namespace AslHelp.Memory.Remote.Win32;

/// <summary>
///     Changes the protection of a range one page at a time, so each page gets its own protection back.
/// </summary>
/// <remarks>
///     One call over a range changes every page but returns only the first page's old protection. Putting that one back
///     on every page leaves a read-only page that follows a writable one writable for good.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed unsafe class PageProtection
{
    private const int PageSize = 0x1000;

    private readonly SafeHandle _process;
    private readonly List<(nuint Page, PAGE_PROTECTION_FLAGS Previous)> _changed = [];

    private PageProtection(SafeHandle process)
    {
        _process = process;
    }

    /// <summary>
    ///     Changes every page the range touches to <paramref name="protection"/>.
    /// </summary>
    /// <remarks>
    ///     A refusal puts back the pages already changed. The error is taken before that, since putting them back
    ///     overwrites it.
    /// </remarks>
    /// <param name="process">The target, opened with the right to change protection.</param>
    /// <param name="address">The first byte of the range.</param>
    /// <param name="length">How many bytes the range covers.</param>
    /// <param name="protection">The protection every page gets.</param>
    /// <param name="changed">What puts each page back, when every page was changed.</param>
    /// <param name="win32Error">The error the system gave for the page it refused, when one was refused.</param>
    /// <returns>
    ///     Whether every page was changed.
    /// </returns>
    public static bool TryChange(
        SafeHandle process,
        nuint address,
        int length,
        PAGE_PROTECTION_FLAGS protection,
        out PageProtection changed,
        out int win32Error)
    {
        changed = new(process);
        win32Error = 0;

        nuint end = address + (nuint)length;

        for (nuint page = address & ~(nuint)(PageSize - 1); page < end; page += PageSize)
        {
            if (!PInvoke.VirtualProtectEx(process, (void*)page, PageSize, protection, out var previous))
            {
                win32Error = Marshal.GetLastWin32Error();
                _ = changed.TryRestore(out _, out _);

                return false;
            }

            changed._changed.Add((page, previous));
        }

        return true;
    }

    /// <summary>
    ///     Puts each changed page's own protection back.
    /// </summary>
    /// <remarks>
    ///     Every page is tried even after one is refused, so one refusal leaves only that page changed.
    /// </remarks>
    /// <param name="refusedAt">The first page the system refused, when one was refused.</param>
    /// <param name="win32Error">The error the system gave for that page.</param>
    /// <returns>
    ///     Whether every page got its protection back.
    /// </returns>
    public bool TryRestore(out nuint refusedAt, out int win32Error)
    {
        refusedAt = 0;
        win32Error = 0;

        foreach (var (page, previous) in _changed)
        {
            if (!PInvoke.VirtualProtectEx(_process, (void*)page, PageSize, previous, out _) && refusedAt == 0)
            {
                win32Error = Marshal.GetLastWin32Error();
                refusedAt = page;
            }
        }

        _changed.Clear();

        return refusedAt == 0;
    }
}
