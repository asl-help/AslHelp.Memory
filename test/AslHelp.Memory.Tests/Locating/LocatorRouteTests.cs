using AslHelp.Memory.Locating;
using AslHelp.Memory.Scanning;
using AslHelp.Memory.Tests.Pe;
using NUnit.Framework;
using System;
using System.Buffers.Binary;

namespace AslHelp.Memory.Tests.Locating;

[TestFixture]
public class LocatorRouteTests
{
    private static readonly Address _base = new(0x400000);

    // An export step returns the export's address plus its offset.
    [Test]
    public void Locate_ExportStep_ReturnsTheExportPlusTheOffset()
    {
        var image = PeImage.Build(true, ["Alpha", "Beta"]);
        BufferMemory memory = new(_base, image.Bytes, MemoryLayout.X64);

        LocatorRoute route = new("release", new ExportStep("Beta", 2));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + image.FunctionRva("Beta") + 2));
    }

    // A missing export fails the step.
    [Test]
    public void Locate_MissingExport_NamesTheStep()
    {
        var image = PeImage.Build(true, ["Alpha"]);
        BufferMemory memory = new(_base, image.Bytes, MemoryLayout.X64);

        var located = new LocatorRoute("release", new ExportStep("Gamma")).Locate(memory, Module(memory));

        Assert.That(located.Error!.Message, Does.Contain("Step 1 (export 'Gamma') failed."));
    }

    // A forward window takes the match nearest after the previous address.
    [Test]
    public void Locate_ForwardStep_TakesTheNearestMatchAhead()
    {
        byte[] bytes = new byte[0x40];
        bytes[0x08] = 0x90;
        bytes[0x20] = 0x90;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        LocatorRoute route = new("release", new OffsetStep(0x04), new SearchStep(ScanWindow.Forward(0x30).Create("90")));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x08));
    }

    // A backward window takes the match nearest before the previous address.
    [Test]
    public void Locate_BackwardStep_TakesTheNearestMatchBehind()
    {
        byte[] bytes = new byte[0x40];
        bytes[0x08] = 0x90;
        bytes[0x20] = 0x90;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        LocatorRoute route = new("release", new OffsetStep(0x30), new SearchStep(ScanWindow.Backward(0x30).Create("90")));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x20));
    }

    // A window without the pattern fails with the pattern and the window.
    [Test]
    public void Locate_WindowWithoutAMatch_NamesThePattern()
    {
        BufferMemory memory = new(_base, new byte[0x40], MemoryLayout.X64);

        var located = new LocatorRoute("release", new SearchStep(ScanWindow.Forward(0x10).Create("AB CD"))).Locate(memory, Module(memory));

        Assert.That(located.Error!.Message, Does.Contain("'AB CD' isn't in the 16 bytes from 0x400000"));
    }

    // A search step's offset moves each match.
    [Test]
    public void Locate_SearchStepWithAnOffset_MovesTheMatch()
    {
        byte[] bytes = new byte[0x40];
        bytes[0x08] = 0x90;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        LocatorRoute route = new("release", new SearchStep(Scan.Create("90"), 3));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x0B));
    }

    // A global read adds the RIP-relative displacement to the end of the instruction.
    [Test]
    public void Locate_GlobalRead_FollowsTheDisplacement()
    {
        byte[] bytes = new byte[0x40];
        new byte[] { 0x48, 0x8B, 0x05 }.CopyTo(bytes, 0x10);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x13), 0x20);
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        LocatorRoute route = new("release", new SearchStep(Scan.Create("48 8B 05"), 3), new ReadStep(ReadKind.Global));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x17 + 0x20));
    }

    // A global read at a pointer size of 4 returns the operand as the absolute address.
    [Test]
    public void Locate_GlobalReadOn32Bit_ReturnsTheAbsoluteOperand()
    {
        byte[] bytes = new byte[0x40];
        bytes[0x10] = 0xA1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x11), 0x401000);
        BufferMemory memory = new(_base, bytes, MemoryLayout.X86);

        LocatorRoute route = new("release", new SearchStep(Scan.Create("A1"), 1), new ReadStep(ReadKind.Global));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(new Address(0x401000)));
    }

    // A displacement read adds the signed distance to the end of the instruction.
    [Test]
    public void Locate_DisplacementRead_FollowsTheDistance()
    {
        byte[] bytes = new byte[0x40];
        bytes[0x20] = 0xE8;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x21), -0x15);
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        LocatorRoute route = new("release", new SearchStep(Scan.Create("E8"), 1), new ReadStep(ReadKind.Displacement));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(_base + 0x10));
    }

    // A pointer read reads the target's pointer size and adds the offset.
    [Test]
    public void Locate_PointerRead_FollowsThePointerPlusTheOffset()
    {
        byte[] bytes = new byte[0x20];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x10), 0xDEAD0);
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        LocatorRoute route = new("release", new OffsetStep(0x10), new ReadStep(Offset: -0x10));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(new Address(0xDEAC0)));
    }

    // A whole-module search whose matches all lead to 1 address succeeds.
    [Test]
    public void Locate_WholeModuleMatchesThatAgree_Succeeds()
    {
        byte[] bytes = new byte[0x40];
        bytes[0x00] = 0x90;
        bytes[0x10] = 0x90;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        LocatorRoute route = new("release", new SearchStep(Scan.Create("90")), new ReadStep(ReadKind.Pointer));

        Assert.That(route.Locate(memory, Module(memory)).Unwrap(), Is.EqualTo(new Address(0x90)));
    }

    // A whole-module search with 2 matches that disagree fails with both addresses.
    [Test]
    public void Locate_WholeModuleMatchesThatDisagree_FailsWithBoth()
    {
        byte[] bytes = new byte[0x40];
        bytes[0x00] = 0x90;
        bytes[0x10] = 0x90;
        BufferMemory memory = new(_base, bytes, MemoryLayout.X64);

        var located = new LocatorRoute("release", new SearchStep(Scan.Create("90"))).Locate(memory, Module(memory));

        Assert.That(located.Error!.Message, Does.Contain("0x400000").And.Contain("0x400010"));
    }

    // A route needs at least 1 step.
    [Test]
    public void Ctor_NoSteps_Throws()
    {
        Assert.That(() => new LocatorRoute("release"), Throws.ArgumentException.With.Message.Contains("at least 1 step"));
    }

    // An export step after the first step throws.
    [Test]
    public void Ctor_ExportAfterTheFirstStep_Throws()
    {
        Assert.That(
            () => new LocatorRoute("release", new ReadStep(), new ExportStep("Beta")),
            Throws.ArgumentException.With.Message.Contains("has to be the first step"));
    }

    // A whole-module search after the first step throws.
    [Test]
    public void Ctor_WholeModuleSearchAfterTheFirstStep_Throws()
    {
        Assert.That(
            () => new LocatorRoute("release", new ReadStep(), new SearchStep(Scan.Create("90"))),
            Throws.ArgumentException.With.Message.Contains("has to be the first step"));
    }

    // A scan step with a resolver can't be a search step.
    [Test]
    public void SearchStep_ScanWithAResolver_Throws()
    {
        Assert.That(
            () => new SearchStep(Scan.Create("90").WithResolver(AddressResolver.FromOffset(1))),
            Throws.ArgumentException.With.Message.Contains("resolver"));
    }

    // A route formats as its name and steps.
    [Test]
    public void ToString_Route_ListsTheSteps()
    {
        LocatorRoute route = new("release", new ExportStep("Beta", 3), new SearchStep(ScanWindow.Backward(0x10).Create("90"), -2), new ReadStep());

        Assert.That(route.ToString(), Is.EqualTo("release: export 'Beta' plus 0x3 then backward 0x10 for '90' minus 0x2 then read pointer"));
    }

    private static MemoryModule Module(BufferMemory memory)
    {
        return new("test.dll", null, memory.Range);
    }
}
