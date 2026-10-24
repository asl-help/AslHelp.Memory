using System.Runtime.InteropServices;

namespace AslHelp.Memory.Pe.Native;

/// <summary>
///     Represents the fields of <c>IMAGE_DOS_HEADER</c> that lead to the NT headers.
/// </summary>
/// <remarks>
///     The other fields of the 64-byte header are left out.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 64)]
internal readonly struct ImageDosHeader
{
    /// <summary>
    ///     The DOS signature <c>MZ</c>, <c>e_magic</c> in the PE format.
    /// </summary>
    [FieldOffset(0)]
    public readonly ushort Magic;

    /// <summary>
    ///     The offset of the NT headers from the module's base address, <c>e_lfanew</c> in the PE format.
    /// </summary>
    [FieldOffset(0x3C)]
    public readonly int Lfanew;
}
