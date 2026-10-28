using System.Runtime.InteropServices;

namespace AslHelp.Memory.Remote.Win32;

/// <summary>
///     Provides the Windows functions the generated bindings leave out.
/// </summary>
/// <remarks>
///     The generator emits <c>VirtualQueryEx</c> only for a specific CPU architecture, because its
///     <c>MEMORY_BASIC_INFORMATION</c> differs between 32-bit and 64-bit hosts. It leaves out
///     <c>GetThreadContext</c> and <c>Wow64GetThreadContext</c> too, because <c>CONTEXT</c> is laid out per
///     architecture.
/// </remarks>
internal static class NativeMethods
{
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern unsafe nuint VirtualQueryEx(
        SafeHandle process,
        void* address,
        out MemoryBasicInformation buffer,
        nuint length);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool GetThreadContext(
        SafeHandle thread,
        byte* context);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool Wow64GetThreadContext(
        SafeHandle thread,
        byte* context);
}
