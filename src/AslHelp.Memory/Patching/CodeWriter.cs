using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Collects the bytes of a stub and measures displacements from where the bytes will be in the target.
/// </summary>
/// <remarks>
///     A jump whose target is not emitted yet is written with a placeholder and overwritten once the target's position is
///     known, so no instruction's length is counted anywhere but where it is emitted.
/// </remarks>
/// <param name="baseAddress">Where the first byte will stand in the target.</param>
internal sealed class CodeWriter(
    Address baseAddress)
{
    private byte[] _bytes = new byte[0xC0];

    /// <summary>
    ///     Gets how many bytes have been emitted.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    ///     Gets the address the next byte will have in the target.
    /// </summary>
    public Address NextAddress => baseAddress + Length;

    public void Emit(params ReadOnlySpan<byte> bytes)
    {
        EnsureCapacity(bytes.Length);

        bytes.CopyTo(_bytes.AsSpan(Length));
        Length += bytes.Length;
    }

    public void EmitInt32(int value)
    {
        Emit(BitConverter.GetBytes(value));
    }

    /// <summary>
    ///     Emits the 4-byte displacement that reaches <paramref name="target"/> from the instruction after this one.
    /// </summary>
    /// <param name="target">Where the displacement reaches.</param>
    /// <param name="trailing">How many bytes of the instruction follow the displacement, e.g. an immediate.</param>
    /// <remarks>
    ///     Keeps the low 32 bits of the distance, because a 32-bit target wraps a relative jump at 4 GB. A 64-bit
    ///     target has to be within 2 GB, which the caller checks before writing the stub.
    /// </remarks>
    public void EmitRelative(Address target, int trailing = 0)
    {
        EmitInt32(unchecked((int)(target - (NextAddress + 4 + trailing))));
    }

    /// <summary>
    ///     Overwrites 1 byte already emitted, for a jump whose target was not known when it was emitted.
    /// </summary>
    /// <param name="offset">Where the byte is, counted from the first byte emitted.</param>
    /// <param name="value">The byte to write.</param>
    public void Overwrite(int offset, byte value)
    {
        _bytes[offset] = value;
    }

    public ReadOnlySpan<byte> WrittenSpan => _bytes.AsSpan(0, Length);

    private void EnsureCapacity(int count)
    {
        if (Length + count > _bytes.Length)
        {
            Array.Resize(ref _bytes, Math.Max(_bytes.Length * 2, Length + count));
        }
    }
}
