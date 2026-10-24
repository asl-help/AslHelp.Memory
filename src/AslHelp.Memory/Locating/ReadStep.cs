using AslHelp.Memory.Scanning;
using System;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Represents the step that reads the address the bytes at the previous address point to.
/// </summary>
/// <param name="Kind">How the bytes encode the next address.</param>
/// <param name="Size">
///     The number of bytes from the operand to the end of its instruction, which a <see cref="ReadKind.Global"/> or
///     <see cref="ReadKind.Branch"/> displacement counts from. A <see cref="ReadKind.Pointer"/> read ignores it.
/// </param>
/// <param name="Offset">The number of bytes to add to the address that was read.</param>
public sealed record ReadStep(
    ReadKind Kind = ReadKind.Pointer,
    int Size = 4,
    int Offset = 0) : RouteStep(Offset)
{
    /// <summary>
    ///     Gets how the bytes encode the next address.
    /// </summary>
    public ReadKind Kind { get; } = Kind is ReadKind.Global or ReadKind.Branch or ReadKind.Pointer
        ? Kind
        : throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "A read is a global, a branch or a pointer.");

    /// <summary>
    ///     Formats the step as in <c>read global plus 0x8</c>.
    /// </summary>
    /// <returns>
    ///     The formatted step.
    /// </returns>
    public override string ToString()
    {
        return $"read {RouteStepConverter.NameOf(Kind)}{OffsetSuffix}";
    }

    private protected override Result<Address> Next(IMemoryReader reader, MemoryModule module, Address from)
    {
        return Kind switch
        {
            ReadKind.Global => AddressResolver.Relative(0, Size)(reader, from),
            ReadKind.Branch => AddressResolver.Displacement(0, Size)(reader, from),
            _ => reader.ReadPointer(from),
        };
    }
}
