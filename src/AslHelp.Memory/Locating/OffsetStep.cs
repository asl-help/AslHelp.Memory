namespace AslHelp.Memory.Locating;

/// <summary>
///     Represents the step that only adds its offset, without reading anything.
/// </summary>
/// <param name="Offset">The number of bytes to add, negative to move back.</param>
public sealed record OffsetStep(
    int Offset) : RouteStep(Offset)
{
    /// <summary>
    ///     Formats the step as in <c>offset 0x10</c>.
    /// </summary>
    /// <returns>
    ///     The formatted step.
    /// </returns>
    public override string ToString()
    {
        return Offset < 0 ? $"offset -0x{-(long)Offset:X}" : $"offset 0x{Offset:X}";
    }

    private protected override Result<Address> Resolve(IMemoryReader reader, MemoryModule module, Address from)
    {
        return from;
    }
}
