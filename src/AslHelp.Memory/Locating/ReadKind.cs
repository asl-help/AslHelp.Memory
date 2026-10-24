using System.Diagnostics.CodeAnalysis;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Specifies how a <see cref="ReadStep"/> turns the bytes at an address into the next address.
/// </summary>
public enum ReadKind
{
    /// <summary>
    ///     The bytes are an operand that references a global, as <see cref="Scanning.AddressResolverExtensions"/>
    ///     reads it with <c>AddressResolver.Relative</c>.
    /// </summary>
    Global,

    /// <summary>
    ///     The bytes are the signed 32-bit distance of a near call or jump, as <c>AddressResolver.Displacement</c>
    ///     reads it.
    /// </summary>
    Branch,

    /// <summary>
    ///     The bytes are a pointer of the target's pointer size.
    /// </summary>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "A route document spells this read 'pointer'.")]
    Pointer,
}
