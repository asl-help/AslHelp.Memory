using System.ComponentModel;

namespace AslHelp.Memory.Remote.Errors;

/// <summary>
///     Represents bytes of another process that couldn't be read.
/// </summary>
/// <remarks>
///     Formats the message on access, because a read that fails is normal, can run every tick and rarely reads it.
/// </remarks>
/// <param name="Address">The address of the first byte.</param>
/// <param name="Size">The number of bytes.</param>
/// <param name="Win32Error">The system error of the read, 0 when Windows didn't report one.</param>
/// <param name="BytesRead">The number of bytes Windows read before it stopped.</param>
internal sealed record RemoteUnreadableError(Address Address, int Size, int Win32Error = 0, nuint BytesRead = 0) : ResultError
{
    /// <inheritdoc/>
    public override string Message => Win32Error != 0
        ? SR.FormatReadFailed(Size, Address, new Win32Exception(Win32Error).Message)
        : BytesRead > 0
            ? SR.FormatReadIncomplete(BytesRead, Size, Address)
            : SR.FormatUnreadable(Size, Address);
}
