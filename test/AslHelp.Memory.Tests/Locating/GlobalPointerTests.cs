using AslHelp.Memory.Locating;
using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System;
using System.Buffers.Binary;

namespace AslHelp.Memory.Tests.Locating;

[TestFixture]
public class GlobalPointerTests
{
    private static readonly Address _base = new(0x400000);

    private static readonly MemoryLocator _locator = new(new LocatorRoute("release", new SearchStep(Scan.Create("90 AB"), 2)));

    // The global is located once and the pointer in it is read fresh on each access.
    [Test]
    public void Read_GlobalRewritten_ReadsTheNewPointer()
    {
        byte[] bytes = Code();
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);
        GlobalPointer pointer = new(memory, Module(memory), _locator, TimeSpan.Zero);

        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x0A), 0x1000);
        var first = pointer.Read().Unwrap();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x0A), 0x2000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(new Address(0x1000)));
            Assert.That(pointer.Read().Unwrap(), Is.EqualTo(new Address(0x2000)));
            Assert.That(pointer.Locate().Unwrap(), Is.EqualTo(_base + 0x0A));
        }
    }

    // A failed locate doesn't search again before the wait has passed.
    [Test]
    public void Read_WithinTheWait_DoesNotSearchAgain()
    {
        CountingMemory memory = new(new(_base, new byte[0x40], MemoryLayout.X64));
        GlobalPointer pointer = new(memory, Module(memory.Inner), _locator, TimeSpan.FromHours(1));

        _ = pointer.Read();
        int reads = memory.Reads;

        for (int i = 0; i < 50; i++)
        {
            _ = pointer.Read();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pointer.Read().IsErr, Is.True);
            Assert.That(memory.Reads, Is.EqualTo(reads));
        }
    }

    // A failed locate is tried again once the wait has passed.
    [Test]
    public void Read_CodeWrittenAfterAFailure_IsFoundOnTheRetry()
    {
        byte[] bytes = new byte[0x40];
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);
        GlobalPointer pointer = new(memory, Module(memory), _locator, TimeSpan.Zero);

        var before = pointer.Read();
        Code().CopyTo(bytes, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Error!.Message, Does.Contain("'release'"));
            Assert.That(pointer.Locate().Unwrap(), Is.EqualTo(_base + 0x0A));
        }
    }

#if NET
    // Reading a located global doesn't allocate anything.
    [Test]
    public void Read_LocatedGlobal_DoesNotAllocate()
    {
        BufferMemory memory = new(_base, Code(), MemoryLayout.X64);
        GlobalPointer pointer = new(memory, Module(memory), _locator, TimeSpan.Zero);
        _ = pointer.Read();

        long before = GC.GetAllocatedBytesForCurrentThread();
        var value = pointer.Read();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.IsOk, Is.True);
            Assert.That(allocated, Is.Zero);
        }
    }
#endif

    private static byte[] Code()
    {
        byte[] bytes = new byte[0x40];
        bytes[0x08] = 0x90;
        bytes[0x09] = 0xAB;

        return bytes;
    }

    private static MemoryModule Module(BufferMemory memory)
    {
        return new("test.dll", null, memory.Range);
    }

    private sealed class CountingMemory(
        BufferMemory inner) : IMemoryReader
    {
        public BufferMemory Inner => inner;

        public int Reads { get; private set; }

        public MemoryLayout Layout => inner.Layout;

        public Result Read(Address address, Span<byte> buffer)
        {
            Reads++;
            return inner.Read(address, buffer);
        }
    }
}
