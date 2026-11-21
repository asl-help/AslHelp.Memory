using System;
using System.Text;

namespace AslHelp.Memory.Scanning;

/// <summary>
///     Represents a byte signature to search a target's memory for, with wildcard nibbles that match any value.
/// </summary>
/// <remarks>
///     <c>default(ScanPattern)</c> has a length of 0 and matches nowhere.
/// </remarks>
public readonly struct ScanPattern : IEquatable<ScanPattern>
{
    private const string HexDigits = "0123456789ABCDEF";

    private readonly byte[]? _values;
    private readonly byte[]? _masks;
    private readonly int _anchor;
    private readonly bool _hasWildcards;

    private ScanPattern(byte[] values, byte[] masks, int anchor, bool hasWildcards)
    {
        _values = values;
        _masks = masks;
        _anchor = anchor;
        _hasWildcards = hasWildcards;
    }

    /// <summary>
    ///     Gets the number of bytes the pattern spans.
    /// </summary>
    public int Length => _values?.Length ?? 0;

    /// <summary>
    ///     Gets the value of each byte, with 0 in each wildcard nibble.
    /// </summary>
    public ReadOnlySpan<byte> Values => _values;

    /// <summary>
    ///     Gets the mask of each byte, with 0xF in each fixed nibble and 0 in each wildcard nibble.
    /// </summary>
    /// <remarks>
    ///     A byte <c>b</c> matches position <c>i</c> when <c>(b &amp; Masks[i]) == Values[i]</c>.
    /// </remarks>
    public ReadOnlySpan<byte> Masks => _masks;

    /// <summary>
    ///     Determines whether 2 patterns match the same bytes.
    /// </summary>
    /// <param name="left">The first pattern.</param>
    /// <param name="right">The second pattern.</param>
    /// <returns>
    ///     <see langword="true"/> when the patterns are equal.
    /// </returns>
    public static bool operator ==(ScanPattern left, ScanPattern right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether 2 patterns match different bytes.
    /// </summary>
    /// <param name="left">The first pattern.</param>
    /// <param name="right">The second pattern.</param>
    /// <returns>
    ///     <see langword="true"/> when the patterns aren't equal.
    /// </returns>
    public static bool operator !=(ScanPattern left, ScanPattern right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Parses <paramref name="signature"/> as pairs of hex digits, as in <c>48 8B 05 ????????</c>.
    /// </summary>
    /// <remarks>
    ///     Whitespace is ignored. Any other character that isn't a hex digit is a wildcard nibble. <c>??</c>,
    ///     <c>xx</c>, <c>..</c> and <c>//</c> each match any byte. <c>4?</c> matches 0x40 to 0x4F.
    /// </remarks>
    /// <param name="signature">The signature to parse.</param>
    /// <returns>
    ///     The pattern.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="signature"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FormatException">
    ///     <paramref name="signature"/> is empty or has an odd number of digits.
    /// </exception>
    public static ScanPattern Parse(string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        if (TryParse(signature, out var pattern))
        {
            return pattern;
        }

        throw new FormatException(CountDigits(signature) == 0
            ? SR.SignatureEmpty
            : SR.FormatSignatureOddDigits(signature));
    }

    /// <summary>
    ///     Tries to parse <paramref name="signature"/> as pairs of hex digits, as in <c>48 8B 05 ????????</c>.
    /// </summary>
    /// <remarks>
    ///     The accepted forms match <see cref="Parse(string)"/>.
    /// </remarks>
    /// <param name="signature">The signature to parse.</param>
    /// <param name="pattern">The pattern, when <paramref name="signature"/> is one.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="signature"/> is a pattern and <see langword="false"/> otherwise.
    /// </returns>
    public static bool TryParse(string? signature, out ScanPattern pattern)
    {
        pattern = default;

        if (signature is null)
        {
            return false;
        }

        int digits = CountDigits(signature);
        if (digits == 0 || digits % 2 != 0)
        {
            return false;
        }

        byte[] values = new byte[digits / 2];
        byte[] masks = new byte[digits / 2];
        bool hasWildcards = false;
        int nibble = 0;

        foreach (char c in signature)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            int value = HexDigits.IndexOf(char.ToUpperInvariant(c));
            int shift = nibble % 2 == 0 ? 4 : 0;

            if (value >= 0)
            {
                values[nibble / 2] |= (byte)(value << shift);
                masks[nibble / 2] |= (byte)(0xF << shift);
            }
            else
            {
                hasWildcards = true;
            }

            nibble++;
        }

        pattern = new(values, masks, FindAnchor(values, masks), hasWildcards);
        return true;
    }

    /// <summary>
    ///     Formats the pattern as uppercase hex pairs with <c>?</c> for each wildcard nibble, as in <c>48 8B ?? 05</c>.
    /// </summary>
    /// <returns>
    ///     The formatted pattern.
    /// </returns>
    public override string ToString()
    {
        if (_values is not { } values || _masks is not { } masks)
        {
            return string.Empty;
        }

        StringBuilder builder = new(values.Length * 3);
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(' ');
            }

            byte mask = masks[i];
            _ = builder.Append((mask & 0xF0) == 0 ? '?' : HexDigits[values[i] >> 4]);
            _ = builder.Append((mask & 0x0F) == 0 ? '?' : HexDigits[values[i] & 0xF]);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Determines whether <paramref name="other"/> matches the same bytes.
    /// </summary>
    /// <param name="other">The pattern to compare to.</param>
    /// <returns>
    ///     <see langword="true"/> when the patterns are equal.
    /// </returns>
    public bool Equals(ScanPattern other)
    {
        return _values.AsSpan().SequenceEqual(other._values)
            && _masks.AsSpan().SequenceEqual(other._masks);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is ScanPattern other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(ToString());
    }

    /// <summary>
    ///     Finds the first offset in <paramref name="data"/> the whole pattern matches at.
    /// </summary>
    /// <param name="data">The bytes to search.</param>
    /// <returns>
    ///     The offset of the match. -1 when the pattern matches nowhere.
    /// </returns>
    internal int IndexOf(ReadOnlySpan<byte> data)
    {
        if (_values is null || data.Length < _values.Length)
        {
            return -1;
        }

        if (!_hasWildcards)
        {
            return data.IndexOf(_values);
        }

        int last = data.Length - _values.Length;

        // No byte is fully fixed, so no byte can be searched for and each offset is checked in turn.
        if (_anchor < 0)
        {
            for (int start = 0; start <= last; start++)
            {
                if (MatchesAt(data, start))
                {
                    return start;
                }
            }

            return -1;
        }

        byte anchor = _values[_anchor];
        for (int start = 0; start <= last;)
        {
            // The vectorized IndexOf skips to the next offset whose anchor byte fits, so the masks are checked only at
            // those candidates.
            int hit = data.Slice(start + _anchor, last - start + 1).IndexOf(anchor);
            if (hit < 0)
            {
                return -1;
            }

            start += hit;
            if (MatchesAt(data, start))
            {
                return start;
            }

            start++;
        }

        return -1;
    }

    private bool MatchesAt(ReadOnlySpan<byte> data, int start)
    {
        byte[] values = _values!;
        byte[] masks = _masks!;

        for (int i = 0; i < values.Length; i++)
        {
            if ((data[start + i] & masks[i]) != values[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int CountDigits(string signature)
    {
        int digits = 0;
        foreach (char c in signature)
        {
            if (!char.IsWhiteSpace(c))
            {
                digits++;
            }
        }

        return digits;
    }

    // An image is padded with 0x00, 0xFF, 0xCC and 0x90, so an anchor on one of them finds a candidate at nearly every
    // offset. The first fully fixed byte outside them is taken, with the first fully fixed byte as the fallback.
    private static int FindAnchor(byte[] values, byte[] masks)
    {
        int first = -1;
        for (int i = 0; i < values.Length; i++)
        {
            if (masks[i] != 0xFF)
            {
                continue;
            }

            if (values[i] is not (0x00 or 0xFF or 0xCC or 0x90))
            {
                return i;
            }

            if (first < 0)
            {
                first = i;
            }
        }

        return first;
    }
}
