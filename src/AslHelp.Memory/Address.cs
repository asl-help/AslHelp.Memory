using System;
using System.Globalization;

namespace AslHelp.Memory;

/// <summary>
///     Represents an address in a target's memory, independent of the host's pointer size.
/// </summary>
/// <remarks>
///     Arithmetic wraps around at the ends of the 64-bit range.
/// </remarks>
/// <param name="Value">The address as an unsigned 64-bit number.</param>
public readonly record struct Address(ulong Value) : IComparable<Address>
{
    /// <summary>
    ///     Gets the address 0.
    /// </summary>
    public static Address Zero { get; }

    /// <summary>
    ///     Moves <paramref name="address"/> by <paramref name="offset"/> bytes.
    /// </summary>
    /// <param name="address">The address to move.</param>
    /// <param name="offset">The number of bytes to move by, negative to move back.</param>
    /// <returns>
    ///     The moved address.
    /// </returns>
    public static Address operator +(Address address, long offset)
    {
        return new(unchecked(address.Value + (ulong)offset));
    }

    /// <summary>
    ///     Moves <paramref name="address"/> back by <paramref name="offset"/> bytes.
    /// </summary>
    /// <param name="address">The address to move.</param>
    /// <param name="offset">The number of bytes to move back by, negative to move forward.</param>
    /// <returns>
    ///     The moved address.
    /// </returns>
    public static Address operator -(Address address, long offset)
    {
        return new(unchecked(address.Value - (ulong)offset));
    }

    /// <summary>
    ///     Compares <paramref name="left"/> to <paramref name="right"/> by value.
    /// </summary>
    /// <param name="left">The first address.</param>
    /// <param name="right">The second address.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="left"/> is lower than <paramref name="right"/>.
    /// </returns>
    public static bool operator <(Address left, Address right)
    {
        return left.Value < right.Value;
    }

    /// <summary>
    ///     Compares <paramref name="left"/> to <paramref name="right"/> by value.
    /// </summary>
    /// <param name="left">The first address.</param>
    /// <param name="right">The second address.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="left"/> is higher than <paramref name="right"/>.
    /// </returns>
    public static bool operator >(Address left, Address right)
    {
        return left.Value > right.Value;
    }

    /// <summary>
    ///     Compares <paramref name="left"/> to <paramref name="right"/> by value.
    /// </summary>
    /// <param name="left">The first address.</param>
    /// <param name="right">The second address.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="left"/> is lower than or equal to <paramref name="right"/>.
    /// </returns>
    public static bool operator <=(Address left, Address right)
    {
        return left.Value <= right.Value;
    }

    /// <summary>
    ///     Compares <paramref name="left"/> to <paramref name="right"/> by value.
    /// </summary>
    /// <param name="left">The first address.</param>
    /// <param name="right">The second address.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="left"/> is higher than or equal to <paramref name="right"/>.
    /// </returns>
    public static bool operator >=(Address left, Address right)
    {
        return left.Value >= right.Value;
    }

    /// <summary>
    ///     Converts <paramref name="value"/> to an address.
    /// </summary>
    /// <param name="value">The address as a number.</param>
    /// <returns>
    ///     The address.
    /// </returns>
    public static explicit operator Address(ulong value)
    {
        return new(value);
    }

    /// <summary>
    ///     Converts <paramref name="address"/> to its value.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>
    ///     The value of the address.
    /// </returns>
    public static explicit operator ulong(Address address)
    {
        return address.Value;
    }

    /// <summary>
    ///     Compares this address to <paramref name="other"/> by value.
    /// </summary>
    /// <param name="other">The address to compare to.</param>
    /// <returns>
    ///     A negative number, 0 or a positive number when this address is lower, equal or higher.
    /// </returns>
    public int CompareTo(Address other)
    {
        return Value.CompareTo(other.Value);
    }

    /// <summary>
    ///     Formats the address as <c>0x</c> followed by uppercase hex, as in <c>0x7FF6A000</c>.
    /// </summary>
    /// <returns>
    ///     The formatted address.
    /// </returns>
    public override string ToString()
    {
        return "0x" + Value.ToString("X", CultureInfo.InvariantCulture);
    }
}
