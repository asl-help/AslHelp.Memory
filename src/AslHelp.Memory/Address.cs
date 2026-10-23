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
public readonly record struct Address(ulong Value) : IComparable<Address>, IComparable, IFormattable
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
    ///     Gets the number of bytes from <paramref name="right"/> to <paramref name="left"/>.
    /// </summary>
    /// <param name="left">The address to measure to.</param>
    /// <param name="right">The address to measure from.</param>
    /// <returns>
    ///     The distance in bytes, negative when <paramref name="left"/> is lower than <paramref name="right"/>.
    /// </returns>
    public static long operator -(Address left, Address right)
    {
        return unchecked((long)(left.Value - right.Value));
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

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">
    ///     <paramref name="obj"/> isn't an <see cref="Address"/>.
    /// </exception>
    int IComparable.CompareTo(object? obj)
    {
        return obj switch
        {
            null => 1,
            Address other => CompareTo(other),
            _ => throw new ArgumentException($"The object isn't an {nameof(Address)}.", nameof(obj)),
        };
    }

    /// <summary>
    ///     Formats the address as <c>0x</c> followed by uppercase hex, as in <c>0x7FF6A000</c>.
    /// </summary>
    /// <returns>
    ///     The formatted address.
    /// </returns>
    public override string ToString()
    {
        return ToString(null, null);
    }

    /// <summary>
    ///     Formats the address's value with <paramref name="format"/>, as in <c>X8</c> for <c>7FF6A000</c>.
    /// </summary>
    /// <param name="format">
    ///     A numeric format for the value. <see langword="null"/> or empty formats as <see cref="ToString()"/> does.
    /// </param>
    /// <param name="formatProvider">The culture information. <see langword="null"/> uses the invariant culture.</param>
    /// <returns>
    ///     The formatted address.
    /// </returns>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        formatProvider ??= CultureInfo.InvariantCulture;

        return string.IsNullOrEmpty(format)
            ? "0x" + Value.ToString("X", formatProvider)
            : Value.ToString(format, formatProvider);
    }

    /// <summary>
    ///     Parses <paramref name="text"/> as an address written as <c>0x</c> hex or as a decimal number.
    /// </summary>
    /// <param name="text">The text to parse, such as <c>0x7FF6A000</c>.</param>
    /// <returns>
    ///     The address.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FormatException">
    ///     <paramref name="text"/> isn't an address.
    /// </exception>
    public static Address Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryParse(text, out var address)
            ? address
            : throw new FormatException($"'{text}' isn't an address. An address is 0x followed by hex or a decimal number.");
    }

    /// <summary>
    ///     Tries to parse <paramref name="text"/> as an address written as <c>0x</c> hex or as a decimal number.
    /// </summary>
    /// <param name="text">The text to parse, such as <c>0x7FF6A000</c>.</param>
    /// <param name="address">The address, when <paramref name="text"/> is one.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="text"/> is an address and <see langword="false"/> otherwise.
    /// </returns>
    public static bool TryParse(string? text, out Address address)
    {
        address = default;

        if (text is null)
        {
            return false;
        }

        bool parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)
            : ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

        address = new(value);

        return parsed;
    }
}
