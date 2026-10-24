using NUnit.Framework;
using System;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class ScratchTests
{
    // A length that fits the stack space slices it instead of renting.
    [Test]
    public void Ctor_LengthFits_SlicesTheStack()
    {
        Span<byte> stack = stackalloc byte[16];
        stack[3] = 0xAB;

        using var scratch = new Scratch<byte>(stack, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scratch.IsRented, Is.False);
            Assert.That(scratch.Buffer.Length, Is.EqualTo(4));
            Assert.That(scratch.Buffer[3], Is.EqualTo(0xAB));
        }
    }

    // A length past the stack space rents from the pool and slices to the exact length.
    [Test]
    public void Ctor_LengthExceeds_RentsExactLength()
    {
        using var scratch = new Scratch<byte>(stackalloc byte[16], 1000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scratch.IsRented, Is.True);
            Assert.That(scratch.Buffer.Length, Is.EqualTo(1000));
        }
    }

    // Dispose returns the rented array, so renting the same size again gives usable space.
    [Test]
    public void Dispose_Rented_ReturnsTheArrayForReuse()
    {
        var first = new Scratch<byte>(default, 4096);
        first.Buffer.Fill(0xCD);
        first.Dispose();

        using var second = new Scratch<byte>(default, 4096);
        second.Buffer.Fill(0xEF);

        Assert.That(second.Buffer[4095], Is.EqualTo(0xEF));
    }

    // A length of 0 gives empty space without renting.
    [Test]
    public void Ctor_ZeroLength_ReturnsEmptyStackSpace()
    {
        using var scratch = new Scratch<char>(default, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scratch.IsRented, Is.False);
            Assert.That(scratch.Buffer.IsEmpty, Is.True);
        }
    }
}
