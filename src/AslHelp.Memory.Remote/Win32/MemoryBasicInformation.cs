using System.Runtime.InteropServices;

using Windows.Win32.System.Memory;

namespace AslHelp.Memory.Remote.Win32;

/// <summary>
///     Represents a <c>MEMORY_BASIC_INFORMATION</c> in the host's pointer size.
/// </summary>
/// <remarks>
///     The 64-bit layout puts <c>PartitionId</c> in the padding after <see cref="AllocationProtect"/>. Pointer-sized
///     fields match the 32-bit and the 64-bit layout.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct MemoryBasicInformation
{
    public nuint BaseAddress;
    public nuint AllocationBase;
    public PAGE_PROTECTION_FLAGS AllocationProtect;
    public nuint RegionSize;
    public VIRTUAL_ALLOCATION_TYPE State;
    public PAGE_PROTECTION_FLAGS Protect;
    public PAGE_TYPE Type;
}
