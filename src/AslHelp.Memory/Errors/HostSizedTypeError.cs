using System;

namespace AslHelp.Memory.Errors;

/// <summary>
///     Represents a read of a type whose size depends on the host.
/// </summary>
/// <remarks>
///     Formats the message on access, because a read that fails is normal, can run every tick and rarely reads it.
/// </remarks>
/// <param name="Type">The host-sized type.</param>
internal sealed record HostSizedTypeError(Type Type) : ResultError
{
    /// <inheritdoc/>
    public override string Message => SR.FormatHostSizedType(Type.Name);
}
