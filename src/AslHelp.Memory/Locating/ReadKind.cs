using System.Diagnostics.CodeAnalysis;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Specifies how a <see cref="ReadStep"/> turns the bytes at an address into the next address.
/// </summary>
public enum ReadKind
{
    /// <summary>
    ///     The bytes are an operand that references a global, as
    ///     <see cref="Scanning.AddressResolverExtensions"/> reads it with <c>AddressResolver.FromRelative</c>. At a
    ///     pointer size of 4 the operand is the absolute address.
    /// </summary>
    Global,

    /// <summary>
    ///     The bytes are the signed 32-bit distance of a near call or jump, as <c>AddressResolver.FromDisplacement</c>
    ///     reads it.
    /// </summary>
    Displacement,

    /// <summary>
    ///     The bytes are a pointer of the target's pointer size.
    /// </summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "A route document spells this read 'pointer'.")]
    Pointer,
}
