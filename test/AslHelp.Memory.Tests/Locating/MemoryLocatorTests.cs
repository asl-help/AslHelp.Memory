using AslHelp.Logging;
using AslHelp.Memory.Locating;
using AslHelp.Memory.Scanning;
using NUnit.Framework;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Locating;

[TestFixture]
public class MemoryLocatorTests
{
    private static readonly Address _base = new(0x400000);

    // A route that succeeds gives the address.
    [Test]
    public void Locate_ARouteSucceeds_TakesIt()
    {
        var memory = Memory(0x08, 0x90);

        MemoryLocator locator = new(
            new LocatorRoute("release", new SearchStep(Scan.For("90"))),
            new LocatorRoute("master", new SearchStep(Scan.For("91"))));

        Assert.That(locator.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x08));
    }

    // A failed route is skipped for one that succeeds.
    [Test]
    public void Locate_ARouteFails_TriesAnother()
    {
        var memory = Memory(0x08, 0x91);

        MemoryLocator locator = new(
            new LocatorRoute("release", new SearchStep(Scan.For("90"))),
            new LocatorRoute("master", new SearchStep(Scan.For("91"))));

        Assert.That(locator.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x08));
    }

    // A whole-module search with disagreeing matches is skipped for another route.
    [Test]
    public void Locate_FirstRouteIsAmbiguous_FallsThroughToTheNext()
    {
        var memory = Memory(0x00, 0x90);
        memory.Write(_base + 0x10, [0x90]).Unwrap();
        memory.Write(_base + 0x20, [0x91]).Unwrap();

        MemoryLocator locator = new(
            new LocatorRoute("release", new SearchStep(Scan.For("90"))),
            new LocatorRoute("master", new SearchStep(Scan.For("91"))));

        Assert.That(locator.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x20));
    }

    // A failure lists every route by name with its error.
    [Test]
    public void Locate_EveryRouteFails_NamesEachRoute()
    {
        var memory = Memory(0x00, 0x00);

        MemoryLocator locator = new(
            new LocatorRoute("release", new SearchStep(Scan.For("90"))),
            new LocatorRoute("master", new SearchStep(Scan.For("91"))));

        Assert.That(
            locator.Locate(memory, Module(memory)).Error!.Message,
            Is.EqualTo(
                "No route located the address. "
                + "'release': Step 1 (search the module for '90') failed. '90' isn't in 'test.dll'. "
                + "'master': Step 1 (search the module for '91') failed. '91' isn't in 'test.dll'."));
    }

    // The logger gets the failed route and the route that located the address.
    [Test]
    public void Locate_WithALogger_WritesEachRoute()
    {
        var memory = Memory(0x08, 0x91);
        RecordingSink sink = new();
        using Logger logger = new();
        logger.Add(sink);

        MemoryLocator locator = new(
            new LocatorRoute("release", new SearchStep(Scan.For("90"))),
            new LocatorRoute("master", new SearchStep(Scan.For("91"))));

        _ = locator.Locate(memory, Module(memory), logger);

        Assert.That(sink.Messages, Has.Some.StartsWith("Route 'release' failed.").And.Some.EqualTo("Route 'master' located 0x400008."));
    }

    // A route picked by name runs on its own and doesn't fall back to another route.
    [Test]
    public void Routes_APickedRoute_RunsOnItsOwn()
    {
        var memory = Memory(0x08, 0x91);

        MemoryLocator locator = new(
            new LocatorRoute("release", new SearchStep(Scan.For("90"))),
            new LocatorRoute("master", new SearchStep(Scan.For("91"))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(locator.Routes["release"].Locate(memory, Module(memory)).IsOk, Is.False);
            Assert.That(locator.Routes["master"].Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x08));
        }
    }

    // A name no route has isn't found.
    [Test]
    public void Routes_AnUnknownName_IsNotFound()
    {
        MemoryLocator locator = new(new LocatorRoute("release", new OffsetStep(0)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(locator.Routes.TryGetValue("master", out _), Is.False);
            Assert.That(() => locator.Routes["master"], Throws.TypeOf<KeyNotFoundException>());
        }
    }

    // 2 routes with 1 name throw.
    [Test]
    public void Ctor_DuplicateNames_Throws()
    {
        Assert.That(
            () => new MemoryLocator(
                new LocatorRoute("release", new OffsetStep(0)),
                new LocatorRoute("release", new OffsetStep(1))),
            Throws.ArgumentException.With.Message.Contains("'release'"));
    }

    // A locator needs at least 1 route.
    [Test]
    public void Ctor_NoRoutes_Throws()
    {
        Assert.That(() => new MemoryLocator(), Throws.ArgumentException);
    }

    // A locator formats as its route names.
    [Test]
    public void ToString_Locator_ListsTheRouteNames()
    {
        MemoryLocator locator = new(
            new LocatorRoute("release", new OffsetStep(0)),
            new LocatorRoute("master", new OffsetStep(1)));

        Assert.That(locator.ToString(), Is.EqualTo("release or master"));
    }

    private static BufferMemory Memory(int at, byte value)
    {
        byte[] bytes = new byte[0x40];
        bytes[at] = value;

        return new(_base, bytes, MemoryLayout.X64);
    }

    private static MemoryModule Module(BufferMemory memory)
    {
        return new("test.dll", null, memory.Range);
    }

    private sealed class RecordingSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Emit(in LogEvent e, int indent)
        {
            Messages.Add(e.Message);
        }

        public void Dispose()
        {
        }
    }
}
