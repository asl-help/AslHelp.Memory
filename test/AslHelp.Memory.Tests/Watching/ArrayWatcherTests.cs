using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;

namespace AslHelp.Memory.Tests.Watching;

[TestFixture]
public class ArrayWatcherTests
{
    private static readonly int[] _before = [1, 2];
    private static readonly int[] _after = [1, 7];

    // A negative length throws when the watcher is created, not on every tick.
    [Test]
    public void Constructor_ANegativeLength_Throws()
    {
        Assert.That(
            () => new ArrayWatcher<int>(new ManualTicks(), Memory(), -1, new Address(0x1000)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // Null offsets throw.
    [Test]
    public void Constructor_NullOffsets_Throws()
    {
        Assert.That(
            () => new ArrayWatcher<int>(new ManualTicks(), Memory(), 1, Address.Zero, null!),
            Throws.ArgumentNullException);
    }

    // The values are read as 1 array.
    [Test]
    public void Current_ARunOfValues_ReadsThemAsOneArray()
    {
        ArrayWatcher<int> watcher = new(new ManualTicks(), Memory(3, 5, 8, 13), 4, Address.Zero);

        Assert.That(watcher.Current, Is.EqualTo([3, 5, 8, 13]));
    }

    // A new array with the same contents isn't a change.
    [Test]
    public void Changed_SameContents_IsFalse()
    {
        ManualTicks ticks = new();
        ArrayWatcher<int> watcher = new(ticks, Memory(3, 5, 8), 3, Address.Zero);

        _ = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.False);
            Assert.That(watcher.Old, Is.EqualTo(watcher.Current));
        }
    }

    // A changed element is a change.
    [Test]
    public void Changed_AnElementChanged_IsTrue()
    {
        var memory = Memory(3, 5, 8);
        ManualTicks ticks = new();
        ArrayWatcher<int> watcher = new(ticks, memory, 3, Address.Zero);

        _ = watcher.Current;
        memory.Write(new Address(sizeof(int)), BitConverter.GetBytes(21)).Unwrap();
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.True);
            Assert.That(watcher.Current, Is.EqualTo([3, 21, 8]));
            Assert.That(watcher.Old, Is.EqualTo([3, 5, 8]));
        }
    }

    // A value past the length isn't read, because the length belongs to the watcher.
    [Test]
    public void Changed_AValuePastTheLength_IsFalse()
    {
        var memory = Memory(3, 5, 8, 13);
        ManualTicks ticks = new();
        ArrayWatcher<int> watcher = new(ticks, memory, 2, Address.Zero);

        _ = watcher.Current;
        memory.Write(new Address(3 * sizeof(int)), BitConverter.GetBytes(99)).Unwrap();
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.False);
            Assert.That(watcher.Current, Is.EqualTo([3, 5]));
        }
    }

    // ChangedTo and ChangedFrom compare the contents.
    [Test]
    public void ChangedTo_TheNewContents_IsTrue()
    {
        var memory = Memory(1, 2);
        ManualTicks ticks = new();
        ArrayWatcher<int> watcher = new(ticks, memory, 2, Address.Zero);

        _ = watcher.Current;
        memory.Write(new Address(sizeof(int)), BitConverter.GetBytes(7)).Unwrap();
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.ChangedTo(_after), Is.True);
            Assert.That(watcher.ChangedFrom(_before), Is.True);
            Assert.That(watcher.ChangedTo(_before), Is.False);
        }
    }

    // An array that can't be read clears the values instead of reporting zeroes.
    [Test]
    public void Current_AnUnreadableArray_IsNull()
    {
        ArrayWatcher<int> watcher = new(new ManualTicks(), Memory(1, 2), 4, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.Null);
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // The format has the element type, the length and the path.
    [Test]
    public void ToString_APath_FormatsTypeLengthAndPath()
    {
        ArrayWatcher<int> watcher = new(new ManualTicks(), Memory(), 4, new Address(0x1000), 0x10);

        Assert.That(watcher.ToString(), Is.EqualTo("Int32[4] at 0x1000 0x10"));
    }

    private static BufferMemory Memory(params int[] values)
    {
        byte[] bytes = new byte[values.Length * sizeof(int)];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i]).CopyTo(bytes, i * sizeof(int));
        }

        return new(Address.Zero, bytes, MemoryLayout.X64);
    }
}
