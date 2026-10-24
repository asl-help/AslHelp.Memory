using System.Runtime.InteropServices;

namespace AslHelp.Memory.Pe.Native;

/// <summary>
///     Represents <c>IMAGE_OPTIONAL_HEADER64</c> without its data directory, which follows it.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct ImageOptionalHeader64
{
    public readonly ushort Magic;
    public readonly byte MajorLinkerVersion;
    public readonly byte MinorLinkerVersion;
    public readonly uint SizeOfCode;
    public readonly uint SizeOfInitializedData;
    public readonly uint SizeOfUninitializedData;
    public readonly uint AddressOfEntryPoint;
    public readonly uint BaseOfCode;
    public readonly ulong ImageBase;
    public readonly uint SectionAlignment;
    public readonly uint FileAlignment;
    public readonly ushort MajorOperatingSystemVersion;
    public readonly ushort MinorOperatingSystemVersion;
    public readonly ushort MajorImageVersion;
    public readonly ushort MinorImageVersion;
    public readonly ushort MajorSubsystemVersion;
    public readonly ushort MinorSubsystemVersion;
    public readonly uint Win32VersionValue;
    public readonly uint SizeOfImage;
    public readonly uint SizeOfHeaders;
    public readonly uint CheckSum;
    public readonly ushort Subsystem;
    public readonly ushort DllCharacteristics;
    public readonly ulong SizeOfStackReserve;
    public readonly ulong SizeOfStackCommit;
    public readonly ulong SizeOfHeapReserve;
    public readonly ulong SizeOfHeapCommit;
    public readonly uint LoaderFlags;
    public readonly uint NumberOfRvaAndSizes;
}
