namespace AslHelp.Memory;

/// <summary>
///     Specifies the encoding of a string in memory.
/// </summary>
public enum StringType
{
    /// <summary>
    ///     One byte per character, ASCII.
    /// </summary>
    Ascii,

    /// <summary>
    ///     UTF-8, with lengths counted in bytes.
    /// </summary>
    Utf8,

    /// <summary>
    ///     UTF-16 in the layout's byte order, with lengths counted in chars.
    /// </summary>
    Utf16,
}
