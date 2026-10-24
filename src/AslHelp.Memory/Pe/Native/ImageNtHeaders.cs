using System.Runtime.InteropServices;

namespace AslHelp.Memory.Pe.Native;

/// <summary>
///     Represents the start of <c>IMAGE_NT_HEADERS32</c> and <c>IMAGE_NT_HEADERS64</c>, up to the optional header's
///     <c>Magic</c>.
/// </summary>
/// <remarks>
///     Both formats share this start. <see cref="Magic"/> picks the optional header that follows.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct ImageNtHeaders
{
    /// <summary>
    ///     The NT signature, <c>PE\0\0</c>.
    /// </summary>
    public readonly uint Signature;

    /// <summary>
    ///     The file header.
    /// </summary>
    public readonly ImageFileHeader FileHeader;

    /// <summary>
    ///     The first field of the optional header, PE32 or PE32+.
    /// </summary>
    public readonly ushort Magic;
}
