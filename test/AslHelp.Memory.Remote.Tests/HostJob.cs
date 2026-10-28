#if !NET11_0_OR_GREATER
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AslHelp.Memory.Remote.Tests;

/// <summary>
///     Provides the job object that kills every child added to it once the test host exits.
/// </summary>
/// <remarks>
///     Stands in for <c>ProcessStartInfo.KillOnParentExit</c>, which only exists on .NET 11 and later. The job handle
///     stays open for the life of the host, so the system closes it and ends the job when the host exits.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class HostJob
{
    private const int ExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private static readonly IntPtr _job = Create();

    /// <summary>
    ///     Adds <paramref name="process"/> to the job, so it ends with the test host.
    /// </summary>
    /// <param name="process">The child to add.</param>
    /// <exception cref="Win32Exception">
    ///     The system refused to add the child to the job.
    /// </exception>
    public static void Add(Process process)
    {
        if (!NativeMethods.AssignProcessToJobObject(_job, process.Handle))
        {
            throw new Win32Exception();
        }
    }

    private static IntPtr Create()
    {
        var job = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);

        if (job == IntPtr.Zero)
        {
            throw new Win32Exception();
        }

        var limits = new ExtendedLimits
        {
            BasicLimits = new() { LimitFlags = KillOnJobClose },
        };

        if (!NativeMethods.SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            throw new Win32Exception();
        }

        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits BasicLimits;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}
#endif
