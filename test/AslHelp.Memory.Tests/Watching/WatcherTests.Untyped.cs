using AslHelp.Memory.Watching;
using NUnit.Framework;

namespace AslHelp.Memory.Tests.Watching;

public partial class WatcherTests
{
    // An unchanged value keeps its box, so untyped access doesn't allocate on each tick.
    [Test]
    public void UntypedCurrent_AnUnchangedValue_ReturnsTheSameObject()
    {
        ManualTicks ticks = new();
        Watcher watcher = new MemoryWatcher<int>(ticks, Memory(5), Address.Zero);

        object? before = watcher.Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.EqualTo(5));
            Assert.That(watcher.Current, Is.SameAs(before));
        }
    }

    // A change moves the box of the value to Old and boxes the new value.
    [Test]
    public void UntypedOld_AfterAChange_IsTheObjectCurrentGaveBefore()
    {
        var memory = Memory(5);
        ManualTicks ticks = new();
        Watcher watcher = new MemoryWatcher<int>(ticks, memory, Address.Zero);

        object? before = watcher.Current;
        ticks.Value++;
        Write(memory, 9);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.SameAs(before).And.EqualTo(5));
            Assert.That(watcher.Current, Is.EqualTo(9));
        }
    }

    // Old is boxed on its first untyped access, so typed access before it doesn't lose the value.
    [Test]
    public void UntypedOld_WithoutAnEarlierUntypedAccess_HasThePreviousValue()
    {
        var memory = Memory(5);
        ManualTicks ticks = new();
        MemoryWatcher<int> watcher = new(ticks, memory, Address.Zero);

        _ = watcher.Current;
        ticks.Value++;
        Write(memory, 9);
        _ = watcher.Current;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((Watcher)watcher).Old, Is.EqualTo(5));
            Assert.That(((Watcher)watcher).Current, Is.EqualTo(9));
        }
    }

    // The box follows the bits of the value, so a comparer that treats 5 and 6 as equal doesn't keep the 5.
    [Test]
    public void UntypedCurrent_AComparerThatTreatsDifferentValuesAsEqual_IsTheLatestValue()
    {
        ManualTicks ticks = new();
        SettableWatcher watcher = new(ticks, new WithinOne()) { Value = 5 };

        _ = ((Watcher)watcher).Current;
        ticks.Value++;
        watcher.Value = 6;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.False);
            Assert.That(watcher.Current, Is.EqualTo(6));
            Assert.That(((Watcher)watcher).Current, Is.EqualTo(6));
        }
    }

    // Each read makes a new array. Untyped Current is that array, not an equal one from an earlier read.
    [Test]
    public void UntypedCurrent_ANewArrayWithEqualContents_IsTheLatestArray()
    {
        ManualTicks ticks = new();
        ArrayWatcher<int> watcher = new(ticks, Memory(5), 1, Address.Zero);

        Assert.That(((Watcher)watcher).Current, Is.SameAs(watcher.Current));

        ticks.Value++;

        Assert.That(((Watcher)watcher).Current, Is.SameAs(watcher.Current));
    }

    // A string is already an object, so untyped Current is the string the latest read made.
    [Test]
    public void UntypedCurrent_AString_IsTheTypedValue()
    {
        byte[] bytes = "hello\0"u8.ToArray();
        BufferMemory memory = new(Address.Zero, bytes, MemoryLayout.X64);
        ManualTicks ticks = new();
        StringWatcher watcher = new(ticks, memory, bytes.Length, StringType.Utf8, Address.Zero);

        Assert.That(((Watcher)watcher).Current, Is.SameAs(watcher.Current));

        ticks.Value++;

        Assert.That(((Watcher)watcher).Current, Is.SameAs(watcher.Current));
    }

    // Clear drops the boxes with the values. The next good read gives the new value.
    [Test]
    public void UntypedCurrent_AfterAFailedReadUnderClear_IsTheDefault()
    {
        var inner = Memory(5);
        SwitchableMemory memory = new(inner);
        ManualTicks ticks = new();
        Watcher watcher = new MemoryWatcher<int>(ticks, memory, Address.Zero);

        _ = watcher.Current;
        ticks.Value++;
        memory.Fail = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.Zero);
            Assert.That(watcher.Old, Is.Zero);
        }

        ticks.Value++;
        memory.Fail = false;
        Write(inner, 9);

        Assert.That(watcher.Current, Is.EqualTo(9));
    }

    // KeepOld keeps the values and their boxes.
    [Test]
    public void UntypedCurrent_AfterAFailedReadUnderKeepOld_KeepsTheObject()
    {
        SwitchableMemory memory = new(Memory(5));
        ManualTicks ticks = new();
        Watcher watcher = new MemoryWatcher<int>(ticks, memory, Address.Zero) { FailAction = FailAction.KeepOld };

        object? before = watcher.Current;
        ticks.Value++;
        memory.Fail = true;

        Assert.That(watcher.Current, Is.SameAs(before).And.EqualTo(5));
    }

    // Typed and untyped access in 1 tick share 1 read.
    [Test]
    public void UntypedCurrent_AfterTypedCurrentInOneTick_ReadsOnce()
    {
        CountingMemory memory = new(Memory(5));
        MemoryWatcher<int> typed = new(new ManualTicks(), memory, Address.Zero);
        Watcher watcher = typed;

        _ = typed.Current;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(5));
            Assert.That(watcher.Old, Is.Zero);
            Assert.That(watcher.Changed, Is.False);
            Assert.That(memory.Reads, Is.EqualTo(1));
        }
    }

    // Untyped access reads, so a failed read under Throw throws there too.
    [Test]
    public void UntypedCurrent_AFailedReadUnderThrow_Throws()
    {
        SwitchableMemory memory = new(Memory(5)) { Fail = true };
        Watcher watcher = new MemoryWatcher<int>(new ManualTicks(), memory, Address.Zero) { FailAction = FailAction.Throw };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => watcher.Current, Throws.InvalidOperationException);
            Assert.That(() => watcher.Old, Throws.InvalidOperationException);
            Assert.That(watcher.Error, Is.Not.Null);
        }
    }
}
