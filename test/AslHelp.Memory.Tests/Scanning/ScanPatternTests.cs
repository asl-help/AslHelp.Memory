using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System;

namespace AslHelp.Memory.Tests.Scanning;

[TestFixture]
public class ScanPatternTests
{
    // Hex pairs and ?? wildcards parse into 1 byte each.
    [Test]
    public void Parse_HexAndWildcards_CountsEachByte()
    {
        var pattern = ScanPattern.Parse("48 8B 05 ????????");

        Assert.That(pattern.Length, Is.EqualTo(7));
        Assert.That(pattern.ToString(), Is.EqualTo("48 8B 05 ?? ?? ?? ??"));
    }

    // Whitespace is ignored and lowercase hex is accepted.
    [Test]
    public void Parse_NoSpacesAndLowercase_FormatsAsUppercasePairs()
    {
        Assert.That(ScanPattern.Parse(" 488b\t05\n").ToString(), Is.EqualTo("48 8B 05"));
    }

    // Any character that isn't hex or whitespace is a wildcard nibble.
    [TestCase("12 xx 56")]
    [TestCase("12 .. 56")]
    [TestCase("12 // 56")]
    [TestCase("12 ?? 56")]
    public void Parse_OtherWildcardForms_EqualsQuestionMarks(string signature)
    {
        Assert.That(ScanPattern.Parse(signature), Is.EqualTo(ScanPattern.Parse("12 ?? 56")));
    }

    // A single wildcard nibble keeps the other nibble fixed.
    [Test]
    public void Parse_AWildcardNibble_KeepsTheOtherNibble()
    {
        Assert.That(ScanPattern.Parse("4? ?F").ToString(), Is.EqualTo("4? ?F"));
    }

    // An odd number of digits throws with a message about the digit count.
    [Test]
    public void Parse_OddDigits_ThrowsFormatException()
    {
        var exception = Assert.Throws<FormatException>(() => ScanPattern.Parse("48 8"));

        Assert.That(exception.Message, Does.Contain("odd number of digits"));
    }

    // An empty or blank signature throws with a message about the empty input.
    [TestCase("")]
    [TestCase("   ")]
    public void Parse_Empty_ThrowsFormatException(string signature)
    {
        var exception = Assert.Throws<FormatException>(() => ScanPattern.Parse(signature));

        Assert.That(exception.Message, Does.Contain("empty"));
    }

    // TryParse returns false for bad input instead of throwing.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("ABC")]
    public void TryParse_BadInput_ReturnsFalse(string? signature)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScanPattern.TryParse(signature, out var pattern), Is.False);
            Assert.That(pattern.Length, Is.Zero);
        }
    }

    // TryParse returns true with the pattern for good input.
    [Test]
    public void TryParse_GoodInput_ReturnsThePattern()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScanPattern.TryParse("E8 ?? ?? ?? ??", out var pattern), Is.True);
            Assert.That(pattern.Length, Is.EqualTo(5));
        }
    }

    // The default pattern has no bytes and formats as an empty string.
    [Test]
    public void ToString_Default_ReturnsEmpty()
    {
        Assert.That(default(ScanPattern).ToString(), Is.Empty);
    }

    // Each wildcard nibble reads as 0 in the values and in the masks, each fixed nibble as its digit and 0xF.
    [Test]
    public void ValuesAndMasks_WildcardNibbles_ReadAsZero()
    {
        var pattern = ScanPattern.Parse("48 4? ?F ??");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pattern.Values.ToArray(), Is.EqualTo(new byte[] { 0x48, 0x40, 0x0F, 0x00 }));
            Assert.That(pattern.Masks.ToArray(), Is.EqualTo(new byte[] { 0xFF, 0xF0, 0x0F, 0x00 }));
        }
    }

    // A pattern without wildcards has a full mask on every byte.
    [Test]
    public void Masks_NoWildcards_AreFull()
    {
        var pattern = ScanPattern.Parse("48 8B 05");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pattern.Values.ToArray(), Is.EqualTo(new byte[] { 0x48, 0x8B, 0x05 }));
            Assert.That(pattern.Masks.ToArray(), Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF }));
        }
    }

    // The default pattern has no values and no masks.
    [Test]
    public void ValuesAndMasks_Default_AreEmpty()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(default(ScanPattern).Values.IsEmpty, Is.True);
            Assert.That(default(ScanPattern).Masks.IsEmpty, Is.True);
        }
    }
}
