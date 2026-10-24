using System.Runtime.InteropServices;

namespace AslHelp.Memory.Pe.Native;

/// <summary>
///     Represents <c>IMAGE_EXPORT_DIRECTORY</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct ImageExportDirectory
{
    public readonly uint Characteristics;
    public readonly uint TimeDateStamp;
    public readonly ushort MajorVersion;
    public readonly ushort MinorVersion;
    public readonly uint Name;
    public readonly uint Base;
    public readonly uint NumberOfFunctions;
    public readonly uint NumberOfNames;
    public readonly uint AddressOfFunctions;
    public readonly uint AddressOfNames;
    public readonly uint AddressOfNameOrdinals;
}
