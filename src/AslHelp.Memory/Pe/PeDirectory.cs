namespace AslHelp.Memory.Pe;

/// <summary>
///     Specifies an entry of the data directory in a module's PE optional header.
/// </summary>
/// <remarks>
///     Each value is the entry's index, as the <c>IMAGE_DIRECTORY_ENTRY_*</c> constants of the PE format number it.
/// </remarks>
public enum PeDirectory
{
    /// <summary>
    ///     The export table, <c>IMAGE_DIRECTORY_ENTRY_EXPORT</c>.
    /// </summary>
    Export = PeFormat.IMAGE_DIRECTORY_ENTRY_EXPORT,

    /// <summary>
    ///     The debug records that point at the module's PDB, <c>IMAGE_DIRECTORY_ENTRY_DEBUG</c>.
    /// </summary>
    Debug = PeFormat.IMAGE_DIRECTORY_ENTRY_DEBUG,
}
