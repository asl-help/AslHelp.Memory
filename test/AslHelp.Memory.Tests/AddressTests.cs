using NUnit.Framework;
using System;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class AddressTests
{
    // Adding an offset moves the address forward by that many bytes.
    [Test]
    public void Add_APositiveOffset_MovesForward()
    {
        Address address = new(0x1000);

        Assert.That(address + 0x10, Is.EqualTo(new Address(0x1010)));
    }

    // Adding a negative offset moves the address back.
    [Test]
    public void Add_ANegativeOffset_MovesBack()
    {
        Address address = new(0x1000);

        Assert.That(address + -0x10, Is.EqualTo(new Address(0xFF0)));
    }

    // Subtracting an offset moves the address back by that many bytes.
    [Test]
    public void Subtract_AnOffset_MovesBack()
    {
        Address address = new(0x1000);

        Assert.That(address - 0x20, Is.EqualTo(new Address(0xFE0)));
    }

    // Addresses compare by their value.
    [Test]
    public void CompareTo_TwoAddresses_OrdersByValue()
    {
        Address low = new(0x10);
        Address high = new(0x20);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(low, Is.LessThan(high));
            Assert.That(high, Is.GreaterThanOrEqualTo(low));
            Assert.That(low.CompareTo(high), Is.Negative);
        }
    }

    // ToString formats the value as uppercase hex with a 0x prefix.
    [Test]
    public void ToString_AnyAddress_FormatsAsUppercaseHex()
    {
        Address address = new(0xDEADBEEF);

        Assert.That(address.ToString(), Is.EqualTo("0xDEADBEEF"));
    }

    // Zero is the address with the value 0.
    [Test]
    public void Zero_Always_HasTheValueZero()
    {
        Assert.That(Address.Zero.Value, Is.Zero);
    }

    // An address converts to and from its ulong value.
    [Test]
    public void ExplicitConversion_AUlong_RoundTrips()
    {
        var address = (Address)0x1234UL;

        Assert.That((ulong)address, Is.EqualTo(0x1234UL));
    }

    // The distance between 2 addresses is a signed number of bytes.
    [Test]
    public void Subtract_AnotherAddress_GivesTheDistance()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new Address(0x1010) - new Address(0x1000), Is.EqualTo(0x10));
            Assert.That(new Address(0x1000) - new Address(0x1010), Is.EqualTo(-0x10));
        }
    }

    // A numeric format applies to the value without the 0x prefix.
    [Test]
    public void ToString_AFormat_FormatsTheValue()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new Address(0xABCD).ToString("X8", null), Is.EqualTo("0000ABCD"));
            Assert.That($"{new Address(0xABCD)}", Is.EqualTo("0xABCD"));
        }
    }

    // An address parses from 0x hex in either case or from a decimal number.
    [TestCase("0x7FF6A000", 0x7FF6A000UL)]
    [TestCase("0X1f", 0x1FUL)]
    [TestCase("4096", 4096UL)]
    public void Parse_AnAddress_ReturnsIt(string text, ulong expected)
    {
        Assert.That(Address.Parse(text), Is.EqualTo(new Address(expected)));
    }

    // Text that isn't an address fails to parse.
    [TestCase("")]
    [TestCase("0x")]
    [TestCase("1F")]
    [TestCase("-1")]
    public void TryParse_TextThatIsNoAddress_ReturnsFalse(string text)
    {
        Assert.That(Address.TryParse(text, out _), Is.False);
    }

    // The non-generic comparison orders addresses like the generic one and refuses other types.
    [Test]
    public void CompareTo_AnObject_OrdersAddressesAndRefusesOthers()
    {
        IComparable address = new Address(0x10);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(address.CompareTo(new Address(0x20)), Is.Negative);
            Assert.That(address.CompareTo(null), Is.Positive);
            Assert.That(() => address.CompareTo(0x20UL), Throws.ArgumentException);
        }
    }
}
