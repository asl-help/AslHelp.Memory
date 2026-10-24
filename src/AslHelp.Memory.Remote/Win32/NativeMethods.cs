using System.Runtime.InteropServices;

namespace AslHelp.Memory.Remote.Win32;

/// <summary>
///     Provides the Windows functions the generated bindings leave out.
/// </summary>
/// <remarks>
///     The generator emits <c>VirtualQueryEx</c> only for a specific CPU architecture, because its
///     <c>MEMORY_BASIC_INFORMATION</c> differs between 32-bit and 64-bit hosts.
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
}
