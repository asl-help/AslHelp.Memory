using System.Runtime.InteropServices;

namespace AslHelp.Memory.Pe.Native;

/// <summary>
///     Represents <c>IMAGE_FILE_HEADER</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct ImageFileHeader
{
    public readonly ushort Machine;
    public readonly ushort NumberOfSections;
    public readonly uint TimeDateStamp;
    public readonly uint PointerToSymbolTable;
    public readonly uint NumberOfSymbols;
    public readonly ushort SizeOfOptionalHeader;
    public readonly ushort Characteristics;
}
