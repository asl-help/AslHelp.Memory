using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace AslHelp.Memory.Tests.Watching;

public partial class WatcherTests
{
    // Untyped Current is the -0.0 from the latest read even though -0.0 equals 0.0.
    [Test]
    public void UntypedCurrent_NegativeZeroAfterZero_IsNegativeZero()
    {
        ManualTicks ticks = new();
        ValueWatcher<double> watcher = new(ticks) { Value = 0.0 };

        _ = ((Watcher)watcher).Current;
        ticks.Value++;
        watcher.Value = -0.0;

        object? current = ((Watcher)watcher).Current;

        Assert.That(BitConverter.DoubleToInt64Bits((double)current!), Is.EqualTo(BitConverter.DoubleToInt64Bits(-0.0)));
    }

    // A null int? boxes to null, so untyped Current and Old are null exactly when the typed value is.
    [Test]
    public void UntypedCurrent_ANullableThatTurnsNullAndBack_FollowsTheTypedValue()
    {
        ManualTicks ticks = new();
        ValueWatcher<int?> watcher = new(ticks) { Value = 5 };
        Watcher untyped = watcher;

        Assert.That(untyped.Current, Is.EqualTo(5));

        ticks.Value++;
        watcher.Value = null;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(untyped.Current, Is.Null);
            Assert.That(untyped.Old, Is.EqualTo(5));
        }

        ticks.Value++;
        watcher.Value = 7;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(untyped.Current, Is.EqualTo(7));
            Assert.That(untyped.Old, Is.Null);
        }
    }

    // A value that changes and changes back moves each box along, so Old is the box of the value in between.
    [Test]
    public void UntypedOld_AfterTwoChanges_IsTheBoxOfTheValueInBetween()
    {
        var memory = Memory(5);
        ManualTicks ticks = new();
        Watcher watcher = new MemoryWatcher<int>(ticks, memory, Address.Zero);

        object? first = watcher.Current;
        ticks.Value++;
        Write(memory, 9);
        object? between = watcher.Current;
        ticks.Value++;
        Write(memory, 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Old, Is.SameAs(between).And.EqualTo(9));
            Assert.That(watcher.Current, Is.Not.SameAs(first).And.EqualTo(5));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // A Read that gets the watcher's own Current sees the last value instead of reading again.
    [Test]
    public void Current_AReadThatGetsItsOwnCurrent_ReadsOncePerTick()
    {
        ManualTicks ticks = new();
        SelfWatcher watcher = new(ticks);

        Assert.That(watcher.Current, Is.EqualTo(1));

        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(2));
            Assert.That(watcher.Calls, Is.EqualTo(2));
        }
    }

    // A comparer that throws leaves the values where they were and sets Error to an ExceptionError.
    [Test]
    public void Error_AComparerThatThrows_IsTheExceptionAndKeepsTheValues()
    {
        ManualTicks ticks = new();
        BrokenComparer comparer = new();
        SettableWatcher watcher = new(ticks, comparer) { Value = 5, FailAction = FailAction.KeepOld };

        _ = watcher.Current;
        ticks.Value++;
        watcher.Value = 6;
        comparer.IsBroken = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => watcher.Current, Throws.InvalidOperationException.With.Message.EqualTo("broken"));
            Assert.That(watcher.Error, Is.TypeOf<ExceptionError>());
        }

        ticks.Value++;
        watcher.Enabled = false;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(5));
            Assert.That(watcher.Old, Is.Zero);
        }
    }

    // A path without offsets is only its address.
    [Test]
    public void ToString_NoOffsets_FormatsOnlyTheAddress()
    {
        MemoryWatcher<int> watcher = new(new ManualTicks(), Memory(0), new Address(0x1000));

        Assert.That(watcher.ToString(), Is.EqualTo("Int32 at 0x1000"));
    }

    // long.MinValue can't be negated as a long, so its magnitude is formatted as an unsigned number.
    [Test]
    public void ToString_TheSmallestOffset_FormatsItsMagnitude()
    {
        MemoryWatcher<int> watcher = new(new ManualTicks(), Memory(0), new Address(0x1000), long.MinValue);

        Assert.That(watcher.ToString(), Is.EqualTo("Int32 at 0x1000 -0x8000000000000000"));
    }
}

// Reads whatever Value is set to.
file sealed class ValueWatcher<T>(
    ITickCounter ticks) : Watcher<T>(ticks)
{
    public T? Value { get; set; }

    protected override Result<T> Read()
    {
        return Value!;
    }
}

// Throws once IsBroken is set.
file sealed class BrokenComparer : IEqualityComparer<int>
{
    public bool IsBroken { get; set; }

    [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "Tests a comparer that throws.")]
    public bool Equals(int x, int y)
    {
        return IsBroken ? throw new InvalidOperationException("broken") : x == y;
    }

    public int GetHashCode(int obj)
    {
        return obj;
    }
}

// Reads its own Current plus 1 and counts the calls.
file sealed class SelfWatcher(
    ITickCounter ticks) : Watcher<int>(ticks)
{
    public int Calls { get; private set; }

    protected override Result<int> Read()
    {
        Calls++;
        return Current + 1;
    }
}
