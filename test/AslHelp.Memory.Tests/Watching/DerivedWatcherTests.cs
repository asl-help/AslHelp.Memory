using AslHelp.Memory.Watching;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Watching;

[TestFixture]
public class DerivedWatcherTests
{
    // A derived watcher reads its tick counter through Ticks.
    [Test]
    public void Current_AWatcherOfItsTickCounter_SeesEachTick()
    {
        ManualTicks ticks = new() { Value = 3 };
        TickWatcher watcher = new(ticks);

        Assert.That(watcher.Current, Is.EqualTo(3));

        ticks.Value = 7;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(7));
            Assert.That(watcher.Old, Is.EqualTo(3));
            Assert.That(watcher.Changed, Is.True);
        }
    }

    // A watcher over a delegate calls it at most once per tick and only in a tick with an access.
    [Test]
    public void Current_TwiceInOneTick_CallsTheSourceOnce()
    {
        ManualTicks ticks = new();
        CallWatcher watcher = new(ticks, () => 5);

        _ = watcher.Current;
        _ = watcher.Current;
        Assert.That(watcher.Calls, Is.EqualTo(1));

        ticks.Value += 3;
        Assert.That(watcher.Calls, Is.EqualTo(1));

        _ = watcher.Current;
        Assert.That(watcher.Calls, Is.EqualTo(2));
    }

    // A watcher over another watcher shares the read of its source in the tick.
    [Test]
    public void Current_OverAnotherWatcher_ReadsTheSourceOncePerTick()
    {
        ManualTicks ticks = new();
        CallWatcher source = new(ticks, () => 5);
        DoubledWatcher watcher = new(ticks, source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(10));
            Assert.That(source.Current, Is.EqualTo(5));
            Assert.That(source.Changed, Is.False);
            Assert.That(source.Calls, Is.EqualTo(1));
        }
    }

    // A failed source gives its error to the watcher over it, which then follows its own FailAction.
    [TestCase(FailAction.Clear, 0)]
    [TestCase(FailAction.KeepOld, 10)]
    public void Error_OverAFailedWatcher_IsTheErrorOfTheSource(FailAction failAction, int expected)
    {
        Result<int> next = 5;
        ManualTicks ticks = new();
        CallWatcher source = new(ticks, () => next);
        DoubledWatcher watcher = new(ticks, source) { FailAction = failAction };

        _ = watcher.Current;
        ticks.Value++;
        GoneError error = new();
        next = error;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(expected));
            Assert.That(watcher.Error, Is.SameAs(error));
            Assert.That(source.Current, Is.Zero);
        }
    }

    // Throw on the watcher over a failed source throws with the error of the source.
    [Test]
    public void Current_OverAFailedWatcherUnderThrow_ThrowsWithTheReasonOfTheSource()
    {
        ManualTicks ticks = new();
        CallWatcher source = new(ticks, () => new GoneError());
        DoubledWatcher watcher = new(ticks, source) { FailAction = FailAction.Throw };

        Assert.That(() => watcher.Current, Throws.InvalidOperationException.With.Message.Contains("The value is gone."));
    }

    // A source that throws sets Error on the watcher over it to an ExceptionError.
    [Test]
    public void Error_OverAWatcherThatThrows_IsTheException()
    {
        ManualTicks ticks = new();
        CallWatcher source = new(ticks, () => new GoneError()) { FailAction = FailAction.Throw };
        DoubledWatcher watcher = new(ticks, source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => watcher.Current, Throws.InvalidOperationException);
            Assert.That(() => watcher.Old, Throws.InvalidOperationException);
            Assert.That(watcher.Error, Is.TypeOf<ExceptionError>());
            Assert.That(watcher.Error?.Message, Does.Contain("The value is gone."));
        }
    }

    // A Read that throws follows FailAction like a failed result.
    [TestCase(FailAction.Clear, 0)]
    [TestCase(FailAction.KeepOld, 5)]
    public void Current_AfterADelegateThatThrows_FollowsFailAction(FailAction failAction, int expected)
    {
        bool broken = false;
        ManualTicks ticks = new();
        CallWatcher watcher = new(ticks, () => broken ? throw new InvalidOperationException("broken") : 5) { FailAction = failAction };

        _ = watcher.Current;
        ticks.Value++;
        broken = true;

        Assert.That(() => watcher.Current, Throws.InvalidOperationException);

        ticks.Value++;
        watcher.Enabled = false;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(expected));
            Assert.That(watcher.Error, Is.TypeOf<ExceptionError>());
        }
    }

    // A watcher disabled in the tick of a read that threw stops throwing at once, because a disabled watcher skips the
    // read.
    [Test]
    public void Current_DisabledInTheTickOfAThrow_DoesNotThrow()
    {
        ManualTicks ticks = new();
        CallWatcher watcher = new(ticks, () => throw new InvalidOperationException("broken")) { FailAction = FailAction.KeepOld };

        Assert.That(() => watcher.Current, Throws.InvalidOperationException);

        watcher.Enabled = false;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => watcher.Current, Throws.Nothing);
            Assert.That(watcher.Error, Is.TypeOf<ExceptionError>());
        }
    }

    // An equal struct with a new string inside keeps its box under the default comparer, because its value didn't change.
    [Test]
    public void UntypedCurrent_AnEqualStructWithANewString_KeepsItsBox()
    {
        ManualTicks ticks = new();
        NamedWatcher watcher = new(ticks);

        object? first = ((Watcher)watcher).Current;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Changed, Is.False);
            Assert.That(((Watcher)watcher).Current, Is.SameAs(first));
        }
    }

    // A source disabled after a failed read keeps its error, so the watcher over it gets that error again.
    [Test]
    public void Error_OverAWatcherDisabledAfterAFailedRead_IsTheOldErrorOfTheSource()
    {
        Result<int> next = new GoneError();
        ManualTicks ticks = new();
        CallWatcher source = new(ticks, () => next);
        DoubledWatcher watcher = new(ticks, source);

        _ = watcher.Current;
        var error = source.Error;
        source.Enabled = false;
        ticks.Value++;
        next = 7;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.Zero);
            Assert.That(watcher.Error, Is.SameAs(error).And.TypeOf<GoneError>());
            Assert.That(source.Calls, Is.EqualTo(1));
        }
    }

    // Error doesn't read, so it doesn't call the source in a new tick.
    [Test]
    public void Error_InANewTick_DoesNotCallTheSource()
    {
        ManualTicks ticks = new();
        CallWatcher watcher = new(ticks, () => new GoneError());

        _ = watcher.Current;
        var error = watcher.Error;
        ticks.Value++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Error, Is.SameAs(error).And.TypeOf<GoneError>());
            Assert.That(watcher.Calls, Is.EqualTo(1));
        }
    }

    // A disabled watcher over another watcher doesn't read its source and keeps its value.
    [Test]
    public void Current_Disabled_DoesNotReadTheSourceWatcher()
    {
        int value = 5;
        ManualTicks ticks = new();
        CallWatcher source = new(ticks, () => value);
        DoubledWatcher watcher = new(ticks, source);

        _ = watcher.Current;
        watcher.Enabled = false;
        ticks.Value++;
        value = 7;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(10));
            Assert.That(watcher.Changed, Is.False);
            Assert.That(source.Calls, Is.EqualTo(1));
        }
    }

    // A disabled source keeps its value, so the watcher over it doesn't see a change or an error.
    [Test]
    public void Changed_OverADisabledWatcher_IsFalse()
    {
        int value = 5;
        ManualTicks ticks = new();
        CallWatcher source = new(ticks, () => value);
        DoubledWatcher watcher = new(ticks, source);

        _ = watcher.Current;
        source.Enabled = false;
        ticks.Value++;
        value = 7;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(10));
            Assert.That(watcher.Changed, Is.False);
            Assert.That(watcher.Error, Is.Null);
            Assert.That(source.Calls, Is.EqualTo(1));
        }
    }

    // Clear resets the values of a watcher over a delegate and the next good value only fills them.
    [Test]
    public void Current_AFailedDelegateUnderClear_ResetsThenFillsAgain()
    {
        Result<int> next = 5;
        ManualTicks ticks = new();
        CallWatcher watcher = new(ticks, () => next);

        _ = watcher.Current;
        ticks.Value++;
        next = new GoneError();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.Zero);
            Assert.That(watcher.Old, Is.Zero);
        }

        ticks.Value++;
        next = 9;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(watcher.Current, Is.EqualTo(9));
            Assert.That(watcher.Changed, Is.False);
        }
    }
}

// The watchers below use public types only, as a watcher outside this library would.

// Reads the tick counter itself.
file sealed class TickWatcher(
    ITickCounter ticks) : Watcher<long>(ticks)
{
    protected override Result<long> Read()
    {
        return Ticks.Value;
    }
}

// Reads whatever the delegate returns and counts the calls.
file sealed class CallWatcher(
    ITickCounter ticks,
    Func<Result<int>> source) : Watcher<int>(ticks)
{
    public int Calls { get; private set; }

    protected override Result<int> Read()
    {
        Calls++;
        return source();
    }
}

// Doubles the value of another watcher.
file sealed class DoubledWatcher(
    ITickCounter ticks,
    Watcher<int> source) : Watcher<int>(ticks)
{
    protected override Result<int> Read()
    {
        int value = source.Current;
        return source.Error is { } error ? error : value * 2;
    }
}

// Reads a struct with an equal string that is a new instance on each read.
file sealed class NamedWatcher(
    ITickCounter ticks) : Watcher<KeyValuePair<string, int>>(ticks)
{
    protected override Result<KeyValuePair<string, int>> Read()
    {
        return new KeyValuePair<string, int>(new string('a', 3), 1);
    }
}

file sealed record GoneError : ResultError
{
    public override string Message => "The value is gone.";
}
