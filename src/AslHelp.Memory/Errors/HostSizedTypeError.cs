using System;

namespace AslHelp.Memory.Errors;

/// <summary>
///     Represents a pointer read into a type whose size depends on the host that doesn't fit in that type.
/// </summary>
/// <remarks>
///     Formats the message on access, because a read that fails is normal, can run every tick and rarely reads it.
/// </remarks>
/// <param name="Type">The host-sized type.</param>
/// <param name="Pointer">The pointer that doesn't fit.</param>
internal sealed record HostSizedTypeError(Type Type, Address Pointer) : ResultError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatHostSizedType(Pointer, Type.Name);
}
