using NUnit.Framework;

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
}
