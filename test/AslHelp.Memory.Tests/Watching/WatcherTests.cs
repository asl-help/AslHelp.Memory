using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Text;

namespace AslHelp.Memory.Tests.Watching;

[TestFixture]
public partial class WatcherTests
{
    // Every access in 1 tick shares 1 read.
    [Test]
    public void Current_TwiceInOneTick_ReadsOnce()
    {
        CountingMemory memory = new(Memory(5));
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(5));
            Assert.That(watcher.Changed, Is.False);
            Assert.That(watcher.Old, Is.Zero);
            Assert.That(memory.Reads, Is.EqualTo(1));
        }
    }

    // A watcher no one accesses doesn't read.
    [Test]
    public void Ticks_WithoutAccess_ReadNothing()
    {
        CountingMemory memory = new(Memory(0));
        ManualTicks ticks = new();
        _ = new MemoryWatcher<int>(ticks, memory, Address.Zero);

        for (int i = 0; i < 10; i++)
        {
            ticks.Value++;
        }

        Assert.That(memory.Reads, Is.Zero);
    }

    // Ticks without an access are skipped, so the comparison is against the last read.
    [Test]
    public void Changed_AfterSkippedTicks_ComparesToTheLastRead()
    {
        var memory = Memory(5);
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero);
        _ = watcher.Current;

        ticks.Value++;
        Write(memory, 7);
        ticks.Value++;
        Write(memory, 9);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.True);
            Assert.That(watcher.Old, Is.EqualTo(5));
            Assert.That(watcher.Current, Is.EqualTo(9));
        }

        ticks.Value++;
        Write(memory, 12);
        ticks.Value++;
        Write(memory, 9);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.False);
            Assert.That(watcher.Old, Is.EqualTo(9));
            Assert.That(watcher.Current, Is.EqualTo(9));
        }
    }

    // Clear resets the values, and the next good read only fills them.
    [Test]
    public void FailedRead_Clear_ResetsTheValues()
    {
        var inner = Memory(5);
        SwitchableMemory memory = new(inner);
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero);
        _ = watcher.Current;

        ticks.Value++;
        memory.Fail = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.Zero);
            Assert.That(watcher.Current, Is.Zero);
            Assert.That(watcher.Changed, Is.False);
        }

        ticks.Value++;
        memory.Fail = false;
        Write(inner, 9);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(9));
            Assert.That(watcher.Changed, Is.False);
        }
    }

    // KeepOld keeps the values, and the next good read compares to them.
    [Test]
    public void FailedRead_KeepOld_KeepsTheValues()
    {
        var inner = Memory(5);
        SwitchableMemory memory = new(inner);
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero) { FailAction = FailAction.KeepOld };
        _ = watcher.Current;

        ticks.Value++;
        Write(inner, 9);
        _ = watcher.Current;

        ticks.Value++;
        memory.Fail = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.EqualTo(5));
            Assert.That(watcher.Current, Is.EqualTo(9));
            Assert.That(watcher.Changed, Is.False);
        }

        ticks.Value++;
        memory.Fail = false;
        Write(inner, 12);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.EqualTo(9));
            Assert.That(watcher.Current, Is.EqualTo(12));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // Throw puts the watcher and the error of the read in the message.
    [Test]
    public void FailedRead_Throw_Throws()
    {
        SwitchableMemory memory = new(Memory(0)) { Fail = true };
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero) { FailAction = FailAction.Throw };

        var exception = Assert.Throws<InvalidOperationException>(() => _ = watcher.Current);

        Assert.That(
            exception.Message,
            Is.EqualTo(
                "Couldn't read Int32 at 0x0. Couldn't read 4 bytes at 0x0. "
                + "Set FailAction to Clear or KeepOld when the value isn't always there."));
    }

    // A failed read throws on every access in the tick, so no access gets the last good value.
    [Test]
    public void FailedRead_Throw_ThrowsOnEveryAccessInTheTick()
    {
        SwitchableMemory memory = new(Memory(99));
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero) { FailAction = FailAction.Throw };

        Assert.That(watcher.Current, Is.EqualTo(99));

        ticks.Value++;
        memory.Fail = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => watcher.Current, Throws.InvalidOperationException);
            Assert.That(() => watcher.Current, Throws.InvalidOperationException);
            Assert.That(() => watcher.Old, Throws.InvalidOperationException);
            Assert.That(() => watcher.Changed, Throws.InvalidOperationException);
        }

        ticks.Value++;
        memory.Fail = false;

        Assert.That(watcher.Current, Is.EqualTo(99));
    }

    // Invalidate lets a read that starts to work partway through a tick run again in the same tick.
    [Test]
    public void Invalidate_AfterAFailedRead_ReadsAgainInTheSameTick()
    {
        ManualTicks ticks = new();
        ReadyLater watcher = new(ticks, Memory(0));

        Assert.That(() => watcher.Current, Throws.InvalidOperationException);

        watcher.MakeReady();

        Assert.That(watcher.Current, Is.EqualTo(1));
    }

    // Check runs the comparison on the previous and the latest value.
    [Test]
    public void Check_AComparison_RunsItOnOldAndCurrent()
    {
        var memory = Memory(5);
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero);
        _ = watcher.Current;

        ticks.Value++;
        Write(memory, 9);

        Assert.That(watcher.Check((old, current) => current > old), Is.True);

        ticks.Value++;
        Write(memory, 3);

        Assert.That(watcher.Check((old, current) => current > old), Is.False);

        ticks.Value++;

        Assert.That(watcher.Check((old, current) => current > old), Is.False);
    }

    // ChangedTo and ChangedFrom are true only in the tick of the change.
    [Test]
    public void ChangedToAndChangedFrom_AValueThatChanged_AreTrueForOneTick()
    {
        var memory = Memory(5);
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero);
        _ = watcher.Current;

        ticks.Value++;
        Write(memory, 9);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.ChangedTo(9), Is.True);
            Assert.That(watcher.ChangedTo(5), Is.False);
            Assert.That(watcher.ChangedFrom(5), Is.True);
            Assert.That(watcher.ChangedFrom(9), Is.False);
        }

        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.ChangedTo(9), Is.False);
            Assert.That(watcher.ChangedFrom(5), Is.False);
        }
    }

    // A disabled watcher doesn't read.
    [Test]
    public void Current_Disabled_DoesNotRead()
    {
        CountingMemory memory = new(Memory(5));
        ManualTicks ticks = new();

        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero) { Enabled = false };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.Zero);
            Assert.That(memory.Reads, Is.Zero);
        }
    }

    // A pointer path is followed before the value is read.
    [Test]
    public void Current_APointerPath_ReadsAtItsEnd()
    {
        byte[] bytes = new byte[0x20];
        BitConverter.GetBytes(0x10UL).CopyTo(bytes, 0);
        BitConverter.GetBytes(42).CopyTo(bytes, 0x18);
        BufferMemory memory = new(Address.Zero, bytes, MemoryLayout.X64);

        MemoryWatcher<int> watcher = new(new ManualTicks(), memory, Address.Zero, 0x8);

        Assert.That(watcher.Current, Is.EqualTo(42));
    }

    // The path is formatted with the address, then each offset, with a minus sign for a negative one.
    [Test]
    public void ToString_APath_FormatsEachOffset()
    {
        MemoryWatcher<int> watcher = new(new ManualTicks(), Memory(0), new Address(0x1000), 0x10, -0x8);

        Assert.That(watcher.ToString(), Is.EqualTo("Int32 at 0x1000 0x10 -0x8"));
    }

    // A string is cut at its terminator and compared by value.
    [Test]
    public void Changed_AStringThatChanged_IsTrue()
    {
        byte[] bytes = "hello\0!!"u8.ToArray();
        BufferMemory memory = new(Address.Zero, bytes, MemoryLayout.X64);
        ManualTicks ticks = new();

        StringWatcher watcher = new(ticks, memory, bytes.Length, StringType.Utf8, Address.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo("hello"));
            Assert.That(watcher.Changed, Is.False);
        }

        ticks.Value++;
        Encoding.UTF8.GetBytes("world\0").CopyTo(bytes, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.True);
            Assert.That(watcher.Old, Is.EqualTo("hello"));
            Assert.That(watcher.Current, Is.EqualTo("world"));
        }
    }

    private static BufferMemory Memory(int value)
    {
        return new(Address.Zero, BitConverter.GetBytes(value), MemoryLayout.X64);
    }

    private static void Write(BufferMemory memory, int value)
    {
        memory.Write(Address.Zero, BitConverter.GetBytes(value)).Unwrap();
    }
}
