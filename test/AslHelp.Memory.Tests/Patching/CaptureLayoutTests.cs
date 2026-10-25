using AslHelp.Memory.Patching;
using NUnit.Framework;
using System;
using CodeWriter = AslHelp.Memory.Patching.CodeWriter;

namespace AslHelp.Memory.Tests.Patching;

[TestFixture]
public class CaptureLayoutTests
{
    private static readonly Address _code = new(0x140010000);
    private static readonly Address _record = new(0x140008000);

    // The payload starts after the 3 header words and fills the rest of the record.
    [Test]
    public void PayloadSize_IsTheRecordPastTheHeader()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CaptureLayout.PayloadAt, Is.EqualTo(0x18));
            Assert.That(CaptureLayout.PayloadSize, Is.EqualTo(0x80 - 0x18));
            Assert.That(CaptureLayout.None.IsEmpty, Is.True);
        }
    }

    // An empty layout emits nothing.
    [Test]
    public void Emit_None_EmitsNothing()
    {
        CodeWriter code = new(_code);

        var result = CaptureLayout.None.Emit(code, _record, StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(code.Length, Is.Zero);
        }
    }

    // Each source emits a store for its item.
    [Test]
    public void Emit_EachSource_EmitsItsStore()
    {
        CodeWriter code = new(_code);
        CaptureLayout layout = new(
        [
            new CaptureItem(CaptureSource.Register, 0, 0x18, 8),
            new CaptureItem(CaptureSource.FloatRegister, 1, 0x20, 4),
            new CaptureItem(CaptureSource.Stack, 4, 0x28, 8, Indirect: true),
        ]);

        var result = layout.Emit(code, _record, StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsOk, Is.True);
            Assert.That(code.Length, Is.GreaterThan(0));
        }
    }

    // 2 items that write over each other are refused before anything is emitted.
    [Test]
    public void Emit_OverlappingItems_FailsWithoutEmitting()
    {
        CodeWriter code = new(_code);
        CaptureLayout layout = new(
        [
            new CaptureItem(CaptureSource.Register, 0, 0x18, 8),
            new CaptureItem(CaptureSource.Register, 1, 0x1C, 4),
        ]);

        var result = layout.Emit(code, _record, StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("overlap"));
            Assert.That(code.Length, Is.Zero);
        }
    }

    // An item outside the payload is refused.
    [TestCase(0x10, 8)] // in the header
    [TestCase(0x7C, 8)] // past the end of the record
    public void Emit_AnItemOutsideThePayload_Fails(int offset, int width)
    {
        CodeWriter code = new(_code);
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, offset, width)]);

        var result = layout.Emit(code, _record, StubAssembler.X64);

        Assert.That(result.Error!.Message, Does.Contain("outside"));
    }

    // A width no single store covers is refused.
    [TestCase(3)]
    [TestCase(16)]
    public void Emit_AWidthNoStoreCovers_Fails(int width)
    {
        CodeWriter code = new(_code);
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, 0x18, width)]);

        Assert.That(layout.Emit(code, _record, StubAssembler.X64).IsErr, Is.True);
    }

    // An 8-byte store doesn't exist on a 32-bit target.
    [Test]
    public void Emit_AWidthBeyondThePointer_Fails()
    {
        CodeWriter code = new(new Address(0x410000));
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, 0x18, 8)]);

        var result = layout.Emit(code, new Address(0x408000), StubAssembler.X86);

        Assert.That(result.Error!.Message, Does.Contain("wider"));
    }

    // An item the target can't capture fails with the assembler's reason.
    [Test]
    public void Emit_ARegisterOn32Bits_Fails()
    {
        CodeWriter code = new(new Address(0x410000));
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, 0x18, 4)]);

        var result = layout.Emit(code, new Address(0x408000), StubAssembler.X86);

        Assert.That(result.Error!.Message, Does.Contain("register"));
    }

    // A layout needs its items.
    [Test]
    public void Constructor_NullItems_Throws()
    {
        Assert.That(() => new CaptureLayout(null!), Throws.ArgumentNullException);
    }
}
