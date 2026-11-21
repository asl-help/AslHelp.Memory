namespace AslHelp.Memory;

/// <summary>
///     Specifies the encoding of a string in memory.
/// </summary>
public enum StringType
{
    /// <summary>
    ///     UTF-16 when the first 2 chars look like Latin text in UTF-16, otherwise UTF-8. A read of a given length looks
    ///     at its first 4 bytes and counts the length in the units of the encoding it picks. When fewer than 4 bytes are
    ///     readable, it reads the length as UTF-8. A read up to a terminator reads UTF-8.
    /// </summary>
    Auto,

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
