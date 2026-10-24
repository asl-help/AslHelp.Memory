namespace AslHelp.Memory.Pe;

/// <summary>
///     Provides the constants of the PE format that the header reads need.
/// </summary>
/// <remarks>
///     The names follow the PE format specification. Offsets count bytes from the start of their structure. Every
///     field of the format is little-endian.
/// </remarks>
internal static class PeFormat
{
    /// <summary>
    ///     The <c>e_magic</c> of a DOS header, <c>MZ</c>.
    /// </summary>
    public const ushort IMAGE_DOS_SIGNATURE = 0x5A4D;

    /// <summary>
    ///     The signature at the start of the NT headers, <c>PE\0\0</c>.
    /// </summary>
    public const uint IMAGE_NT_SIGNATURE = 0x00004550;

    /// <summary>
    ///     The optional header magic of a PE32 image.
    /// </summary>
    public const ushort IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10B;

    /// <summary>
    ///     The optional header magic of a PE32+ image.
    /// </summary>
    public const ushort IMAGE_NT_OPTIONAL_HDR64_MAGIC = 0x20B;

    /// <summary>
    ///     The index of the export table in the data directory.
    /// </summary>
    public const int IMAGE_DIRECTORY_ENTRY_EXPORT = 0;

    /// <summary>
    ///     The index of the debug records in the data directory.
    /// </summary>
    public const int IMAGE_DIRECTORY_ENTRY_DEBUG = 6;

    /// <summary>
    ///     The largest number of entries in the data directory.
    /// </summary>
    public const int IMAGE_NUMBEROF_DIRECTORY_ENTRIES = 16;
}
