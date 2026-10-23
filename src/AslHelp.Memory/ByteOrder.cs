namespace AslHelp.Memory;

/// <summary>
///     Specifies the order of the bytes of a multi-byte value in memory.
/// </summary>
public enum ByteOrder
{
    /// <summary>
    ///     The lowest byte comes first.
    /// </summary>
    LittleEndian,

    /// <summary>
    ///     The highest byte comes first.
    /// </summary>
    BigEndian,
}
