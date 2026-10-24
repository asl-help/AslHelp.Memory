using System.Runtime.InteropServices;

namespace AslHelp.Memory.Pe.Native;

/// <summary>
///     Represents <c>IMAGE_DATA_DIRECTORY</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct ImageDataDirectory
{
    public readonly uint VirtualAddress;
    public readonly uint Size;
}
