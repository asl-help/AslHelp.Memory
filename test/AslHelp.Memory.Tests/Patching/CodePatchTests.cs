using AslHelp.Memory.Patching;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Patching;

// Tests stub composition and the read protocol without writing into a process. The stubs are compared as bytes and a
// byte array stands in for the target's record that a patch is read from.
[TestFixture]
public partial class CodePatchTests
{
    private static readonly Address _entry = new(0x140001000);
    private static readonly Address _slot = new(0x140010000);
    private static readonly Address _record = new(0x140008000);

    private static readonly Address _entry32 = new(0x400000);
    private static readonly Address _slot32 = new(0x410000);
    private static readonly Address _record32 = new(0x408000);

    // Any bytes do, because composing displaces a prologue without reading it.
    private static readonly byte[] _prologue = [0x48, 0x83, 0xEC, 0x28, 0x90];
    private static readonly byte[] _prologue32 = [0x55, 0x8B, 0xEC, 0x83, 0xEC];

    // The longest run of instructions a patch ever displaces, which a stub has to fit beside.
    private static readonly byte[] _widest = new byte[24];

    // The 19 bytes between the displaced prologue and the start of the stub.
    private const string HeaderTail = "00000000000000000000000000000000000000";

    [Test]
    public void Assemble_AnEmptyLayout_WritesTheCountingStubUnchanged()
    {
        byte[] composed = CodePatch.Assemble(_entry, _slot, _record, _prologue, CaptureLayout.None, StubAssembler.X64)
            .Unwrap();

        byte[] expected = FromHex(
            "0080004001000000", // the record this patch counts into
            "41534C50", // the marker a later run recognizes the page by
            "0200", // the version, which decides how much of the record means something
            "05", // how many bytes the entry was patched with
            "05", // how many it displaced
            "0010004001000000", // the entry it claims
            "4883EC2890", // the bytes it displaced
            HeaderTail,
            "F048FF05C87FFFFF", // lock inc qword [rip+disp32], reaching the record's first word
            "4883EC2890", // the displaced prologue
            "E9C30FFFFF");          // and back to the instruction after the patch

        Assert.That(composed, Is.EqualTo(expected));
    }

    [Test]
    public void Assemble_AnEmptyLayoutOnThirtyTwoBits_WritesTheCountingStubUnchanged()
    {
        byte[] composed = CodePatch.Assemble(_entry32, _slot32, _record32, _prologue32, CaptureLayout.None, StubAssembler.X86)
            .Unwrap();

        byte[] expected = FromHex(
            "0080400000000000",
            "41534C50",
            "0200",
            "05",
            "05",
            "0000400000000000",
            "558BEC83EC",
            HeaderTail,
            "F0FF0500804000", // lock inc dword [disp32], the record given outright
            "558BEC83EC",
            "E9C4FFFEFF");

        Assert.That(composed, Is.EqualTo(expected));
    }

    [Test]
    public void Assemble_ACapturingLayout_ClaimsTheSequenceAndSendsASecondCallerPastTheCapture()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, CaptureLayout.PayloadOffset, 8)]);

        byte[] composed = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64).Unwrap();

        byte[] expected = FromHex(
            "0080004001000000",
            "41534C50",
            "0200",
            "05",
            "05",
            "0010004001000000",
            "4883EC2890",
            HeaderTail,
            "F048FF05C87FFFFF", // the count first, so a payload caught whole belongs to a call it includes
            "F0480FBA2DC67FFFFF00", // lock bts [rip+disp32], 0: the sequence to odd, with the carry flag set for the thread that got there first
            "720F", // jc past the capture to the resume, for the thread that did not
            "48890DCD7FFFFF", // mov [rip+disp32], rcx, the first argument into the payload
            "F048FF05B57FFFFF", // the sequence back to even, which is what a reader waits for
            "4883EC2890",
            "E9A80FFFFF");

        Assert.That(composed, Is.EqualTo(expected));
    }

    // An indirect register capture leaves a null alone. Reading through a captured value is the one thing a stub does
    // that can fault. A slot at zero is ordinary, so the load is jumped over and the record keeps the zero.
    [Test]
    public void Assemble_AnIndirectRegisterCapture_LeavesANullAloneRatherThanReadingThroughIt()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, CaptureLayout.PayloadOffset, 8, IsIndirect: true)]);

        byte[] composed = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64).Unwrap();

        byte[] expected = FromHex(
            "0080004001000000",
            "41534C50",
            "0200",
            "05",
            "05",
            "0010004001000000",
            "4883EC2890",
            HeaderTail,
            "F048FF05C87FFFFF",
            "F0480FBA2DC67FFFFF00",
            "721B", // past the capture, for a thread that found the sequence claimed
            "50", // the accumulator, which is where the value is read into
            "31C0", // zeroed, so a null pointer captures as nothing
            "4885C9", // test rcx, rcx
            "7403", // and past the load when it does
            "488B01", // mov rax, [rcx]
            "488905C27FFFFF", // whichever of the 2 reached the payload
            "58",
            "F048FF05A97FFFFF", // the sequence back to even
            "4883EC2890",
            "E99C0FFFFF");

        Assert.That(composed, Is.EqualTo(expected));
    }

    [Test]
    public void Assemble_AnIndirectStackCapture_LeavesANullAloneRatherThanReadingThroughIt()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, CaptureLayout.PayloadOffset, 8, IsIndirect: true)]);

        byte[] composed = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64).Unwrap();

        byte[] expected = FromHex(
            "0080004001000000",
            "41534C50",
            "0200",
            "05",
            "05",
            "0010004001000000",
            "4883EC2890",
            HeaderTail,
            "F048FF05C87FFFFF",
            "F0480FBA2DC67FFFFF00",
            "7221",
            "50",
            "488B842410000000", // mov rax, [rsp+disp32], the slot itself
            "4885C0", // which is already the zero a null captures as
            "7403",
            "488B00", // mov rax, [rax]
            "488905BC7FFFFF",
            "58",
            "F048FF05A37FFFFF",
            "4883EC2890",
            "E9960FFFFF");

        Assert.That(composed, Is.EqualTo(expected));
    }

    [Test]
    public void Assemble_AnIndirectStackCaptureOnThirtyTwoBits_LeavesANullAloneToo()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, CaptureLayout.PayloadOffset, 4, IsIndirect: true)]);

        byte[] composed = CodePatch.Assemble(_entry32, _slot32, _record32, _prologue32, layout, StubAssembler.X86).Unwrap();

        byte[] expected = FromHex(
            "0080400000000000",
            "41534C50",
            "0200",
            "05",
            "05",
            "0000400000000000",
            "558BEC83EC",
            HeaderTail,
            "F0FF0500804000",
            "F00FBA2D0880400000",
            "721B",
            "50",
            "8B842408000000", // mov eax, [esp+disp32]
            "85C0", // test eax, eax
            "7402", // past a 2-byte load rather than a 3-byte one
            "8B00", // mov eax, [eax]
            "A318804000",
            "58",
            "F0FF0508804000",
            "558BEC83EC",
            "E99EFFFEFF");

        Assert.That(composed, Is.EqualTo(expected));
    }

    // Each of several captures addresses a different slot from a different position. Every store addresses the record
    // relative to the store, so no 2 displacements are equal. Each dereference restores the accumulator, so the
    // next item addresses against the entry's stack pointer.
    [Test]
    public void Assemble_SeveralCaptures_ReachesEachOwnPlaceFromItsOwnPosition()
    {
        CaptureLayout layout = new(
        [
            new CaptureItem(CaptureSource.Register, 0, CaptureLayout.PayloadOffset, 8),
            new CaptureItem(CaptureSource.Register, 0, CaptureLayout.PayloadOffset + 0x08, 8, IsIndirect: true),
            new CaptureItem(CaptureSource.Register, 1, CaptureLayout.PayloadOffset + 0x10, 4),
            new CaptureItem(CaptureSource.Register, 2, CaptureLayout.PayloadOffset + 0x18, 8),
            new CaptureItem(CaptureSource.Register, 2, CaptureLayout.PayloadOffset + 0x20, 8, IsIndirect: true),
        ]);

        byte[] composed = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64).Unwrap();

        byte[] expected = FromHex(
            "0080004001000000",
            "41534C50",
            "0200",
            "05",
            "05",
            "0010004001000000",
            "4883EC2890",
            HeaderTail,
            "F048FF05C87FFFFF",
            "F0480FBA2DC67FFFFF00",
            "7242", // past the capture, for a thread that found the sequence claimed
            "48890DCD7FFFFF", // mov [rip+disp32], rcx
            "50", // the accumulator, borrowed for the dereference and given back
            "31C0",
            "4885C9", // test rcx, rcx
            "7403",
            "488B01", // mov rax, [rcx]
            "488905C37FFFFF", // 8 bytes further into the record than the pointer above it
            "58",
            "8915C47FFFFF", // mov [rip+disp32], edx, 4 bytes for a 4-byte argument
            "4C8905C57FFFFF", // mov [rip+disp32], r8
            "50",
            "31C0",
            "4D85C0", // test r8, r8
            "7403",
            "498B00", // mov rax, [r8]
            "488905BB7FFFFF",
            "58",
            "F048FF05827FFFFF", // the sequence back to even
            "4883EC2890",
            "E9750FFFFF");

        Assert.That(composed, Is.EqualTo(expected));
    }

    // A narrow register capture stores only its declared width. A register has 8 bytes whatever the argument was
    // declared as. Both runtimes were measured leaving something plausible above a narrower one. The skip reaches
    // the resume, over the store and the sequence going back to even.
    [TestCase(4, "720E", "8915CE7FFFFF", "F048FF05B67FFFFF", "F048FF05C87FFFFF", "E9A90FFFFF")]
    [TestCase(2, "720F", "668915CD7FFFFF", "F048FF05B57FFFFF", "F048FF05C87FFFFF", "E9A80FFFFF")]
    [TestCase(1, "720E", "8815CE7FFFFF", "F048FF05B67FFFFF", "F048FF05C87FFFFF", "E9A90FFFFF")]
    public void Assemble_ANarrowArgument_WritesItAtTheWidthItWasDeclared(
        int width,
        string skip,
        string store,
        string sequence,
        string count,
        string back)
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 1, CaptureLayout.PayloadOffset, width)]);

        byte[] composed = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64).Unwrap();

        byte[] expected = FromHex(
            "0080004001000000",
            "41534C50",
            "0200",
            "05",
            "05",
            "0010004001000000",
            "4883EC2890",
            HeaderTail,
            count,
            "F0480FBA2DC67FFFFF00",
            skip,
            store,
            sequence,
            "4883EC2890",
            back);

        Assert.That(composed, Is.EqualTo(expected));
    }

    // 2 captures on the same bytes are refused. The record would have a number either could have written, and the
    // layout is the only place that sees every item at once.
    [Test]
    public void Assemble_TwoCapturesOnTheSameBytes_RefusesThem()
    {
        CaptureLayout layout = new(
        [
            new CaptureItem(CaptureSource.Register, 0, CaptureLayout.PayloadOffset, 8),
            new CaptureItem(CaptureSource.Register, 1, CaptureLayout.PayloadOffset + 4, 4),
        ]);

        var result = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("overlap"));
            Assert.That(result.Error!.Message, Does.Contain($"0x{CaptureLayout.PayloadOffset:X}"));
            Assert.That(result.Error!.Message, Does.Contain($"0x{CaptureLayout.PayloadOffset + 4:X}"));
        }
    }

    [Test]
    public void Assemble_AWidthNoSingleStoreCovers_RefusesIt()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, CaptureLayout.PayloadOffset, 3)]);

        var result = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("3 bytes"));
        }
    }

    [Test]
    public void Assemble_AnEightByteCaptureOnAThirtyTwoBitTarget_RefusesIt()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Stack, 0, CaptureLayout.PayloadOffset, 8)]);

        var result = CodePatch.Assemble(_entry32, _slot32, _record32, _prologue32, layout, StubAssembler.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("wider than the 4"));
        }
    }

    // All 4 registers copied and dereferenced still fit a slot. That is the most an entry can be asked for while its
    // arguments are in registers. It comes to 138 of the 144 bytes a slot leaves for code. The displaced prologue
    // eats the margin. The same captures over 12 displaced bytes want 1 byte more.
    [Test]
    public void Assemble_EveryRegisterCopiedAndDereferenced_StaysInsideASlot()
    {
        byte[] composed = CodePatch.Assemble(_entry, _slot, _record, _prologue, CreateAllRegistersLayout(), StubAssembler.X64)
            .Unwrap();

        Assert.That(composed, Has.Length.EqualTo(0x30 + 142));
    }

    [Test]
    public void Assemble_TheSameCapturesOverALongerPrologue_RefusesThemRatherThanWritingPastTheSlot()
    {
        // `sub rsp, 0x28`, `mov [rsp+0x30], rbx` and `mov rbx, rcx`, 12 bytes of an ordinary prologue and 7
        // more than the 5 a jump needs.
        byte[] prologue = [0x48, 0x83, 0xEC, 0x28, 0x48, 0x89, 0x5C, 0x24, 0x30, 0x48, 0x8B, 0xD9];

        var result = CodePatch.Assemble(_entry, _slot, _record, prologue, CreateAllRegistersLayout(), StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("149 bytes"));
            Assert.That(result.Error!.Message, Does.Contain($"room for {CodePatch.SlotSize - 0x30}"));
        }
    }

    private static CaptureLayout CreateAllRegistersLayout()
    {
        List<CaptureItem> items = [];

        for (int i = 0; i < 4; i++)
        {
            int at = CaptureLayout.PayloadOffset + (i * 16);

            items.Add(new CaptureItem(CaptureSource.Register, i, at, 8));
            items.Add(new CaptureItem(CaptureSource.Register, i, at + 8, 8, IsIndirect: true));
        }

        return new CaptureLayout(items);
    }

    [Test]
    public void Assemble_AStubTooLargeForItsSlot_RefusesItRatherThanWritingPastIt()
    {
        List<CaptureItem> items = [];

        for (int i = 0; i < 8; i++)
        {
            items.Add(new CaptureItem(CaptureSource.Stack, i, CaptureLayout.PayloadOffset + (i * 8), 8, IsIndirect: true));
        }

        var result = CodePatch.Assemble(_entry, _slot, _record, _prologue, new CaptureLayout(items), StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);

            // The error reports both sizes, so a caller sees how far over the slot the layout went, not only that the
            // layout went over.
            Assert.That(result.Error!.Message, Does.Contain("238 bytes"));
            Assert.That(result.Error!.Message, Does.Contain($"room for {CodePatch.SlotSize - 0x30}"));
        }
    }

    // `StubFitsSlot` checks against the widest prologue, not the prologue at hand, since a caller composing a layout
    // can't know where the slot's bound is and the result has to be true for every entry.
    [Test]
    public void StubFitsSlot_ALayoutNearFillingASlot_AnswersForTheWidestPrologueRatherThanTheOneAtHand()
    {
        int accepted = 0;

        while (accepted < 8 && CodePatch.StubFitsSlot(CreateObjectLayout(accepted + 1), StubAssembler.X64))
        {
            accepted++;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CodePatch.StubFitsSlot(CaptureLayout.None, StubAssembler.X64), Is.True);
            Assert.That(accepted, Is.EqualTo(3));

            // The count `StubFitsSlot` accepted assembles and 1 more object does not, so `StubFitsSlot` and `Assemble`
            // agree.
            Assert.That(CodePatch.Assemble(_entry, _slot, _record, _widest, CreateObjectLayout(accepted), StubAssembler.X64).IsOk, Is.True);

            Assert.That(CodePatch.Assemble(_entry, _slot, _record, _widest, CreateObjectLayout(accepted + 1), StubAssembler.X64).IsErr, Is.True);
        }
    }

    [Test]
    public void StubFitsSlot_WithoutALayout_Throws()
    {
        Assert.That(() => CodePatch.StubFitsSlot(null!, StubAssembler.X64), Throws.ArgumentNullException);
    }

    // Builds a layout of `count` objects, each captured as its address and the word the object opens with.
    private static CaptureLayout CreateObjectLayout(int count)
    {
        List<CaptureItem> items = [];

        for (int i = 0; i < count; i++)
        {
            int at = CaptureLayout.PayloadOffset + (i * 16);

            items.Add(new CaptureItem(CaptureSource.Register, i % 4, at, 8));
            items.Add(new CaptureItem(CaptureSource.Register, i % 4, at + 8, 8, IsIndirect: true));
        }

        return new CaptureLayout(items);
    }

    [Test]
    public void Assemble_ARegisterOnAThirtyTwoBitTarget_RefusesIt()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, CaptureLayout.PayloadOffset, 4)]);

        var result = CodePatch.Assemble(_entry32, _slot32, _record32, _prologue32, layout, StubAssembler.X86);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("on the stack"));
        }
    }

    [Test]
    public void Assemble_AnArgumentPastTheFourthRegister_RefusesIt()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 4, CaptureLayout.PayloadOffset, 8)]);

        var result = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("Argument 4"));
        }
    }

    // A capture aimed at the record's own words is refused. The count and the sequence live below the payload. A
    // capture there would overwrite what the reader reads by.
    [Test]
    public void Assemble_ACaptureAimedAtTheRecordsOwnWords_RefusesIt()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, 0x08, 8)]);

        var result = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("payload"));
        }
    }

    [Test]
    public void Assemble_ACaptureRunningPastTheRecord_RefusesIt()
    {
        CaptureLayout layout = new([new CaptureItem(CaptureSource.Register, 0, CaptureLayout.RecordSize - 4, 8)]);

        var result = CodePatch.Assemble(_entry, _slot, _record, _prologue, layout, StubAssembler.X64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsErr, Is.True);
            Assert.That(result.Error!.Message, Does.Contain("payload"));
        }
    }

    // A plan steals whole instructions at the entry and keeps their bytes.
    [Test]
    public void CreatePlan_AMonoX64Prologue_StealsTheFirstInstruction()
    {
        var memory = CreateMemory(_entry, FromHex("48895C2408", "48896C2410", "4889742418", "57", "4883EC20"), MemoryLayout.X64);

        var plan = CodePatch.CreatePlan(memory, _entry, CaptureLayout.None).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Entry, Is.EqualTo(_entry));
            Assert.That(plan.Original, Is.EqualTo(FromHex("48895C2408")));
            Assert.That(plan.Layout, Is.SameAs(CaptureLayout.None));
            Assert.That(plan.Assembler, Is.SameAs(StubAssembler.X64));
        }
    }

    // A 32-bit target gets the x86 assembler and steals past the jump's 5 bytes.
    [Test]
    public void CreatePlan_AnMsvcX86Prologue_StealsWholeInstructions()
    {
        var memory = CreateMemory(_entry32, FromHex("55", "8BEC", "83EC08", "53", "56", "57"), MemoryLayout.X86);

        var plan = CodePatch.CreatePlan(memory, _entry32, CaptureLayout.None).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Original, Is.EqualTo(FromHex("55", "8BEC", "83EC08")));
            Assert.That(plan.Assembler, Is.SameAs(StubAssembler.X86));
        }
    }

    // An entry whose stolen instructions depend on their address is refused.
    [Test]
    public void CreatePlan_ARipRelativePrologue_Refuses()
    {
        var memory = CreateMemory(_entry, FromHex("488B05E9B95500", "4889484048"), MemoryLayout.X64);

        var result = CodePatch.CreatePlan(memory, _entry, CaptureLayout.None);

        Assert.That(result.Error!.Message, Does.Contain("depend on their address"));
    }

    // An entry whose first 5 bytes cross an 8-byte boundary can't take the jump in 1 store.
    [Test]
    public void CreatePlan_AnEntryCrossingAWord_Refuses()
    {
        var entry = _entry + 4;
        var memory = CreateMemory(entry, FromHex("48895C2408", "4883EC20"), MemoryLayout.X64);

        var result = CodePatch.CreatePlan(memory, entry, CaptureLayout.None);

        Assert.That(result.Error!.Message, Does.Contain("boundary"));
    }

    // An entry that can't be decoded is refused.
    [Test]
    public void CreatePlan_AnUndecodableEntry_Refuses()
    {
        var memory = CreateMemory(_entry, FromHex("0F3800C0"), MemoryLayout.X64);

        var result = CodePatch.CreatePlan(memory, _entry, CaptureLayout.None);

        Assert.That(result.Error!.Message, Does.Contain("decoded"));
    }

    // An entry with fewer readable bytes than the widest prologue is refused.
    [Test]
    public void CreatePlan_AnUnreadableEntry_Refuses()
    {
        BufferMemory memory = new(_entry, new byte[4], MemoryLayout.X64);

        var result = CodePatch.CreatePlan(memory, _entry, CaptureLayout.None);

        Assert.That(result.Error!.Message, Does.Contain("read"));
    }

    // A target with a pointer size other than 4 or 8 isn't Intel and is refused.
    [Test]
    public void CreatePlan_ANonIntelTarget_Refuses()
    {
        var memory = CreateMemory(_entry, FromHex("48895C2408", "4883EC20"), new MemoryLayout(2, ByteOrder.LittleEndian));

        var result = CodePatch.CreatePlan(memory, _entry, CaptureLayout.None);

        Assert.That(result.Error!.Message, Does.Contain("x86 and x64"));
    }

    // A plan refuses a layout its stub can't fit, before any memory is reserved.
    [Test]
    public void CreatePlan_ALayoutTooLargeForASlot_Refuses()
    {
        var memory = CreateMemory(_entry, FromHex("48895C2408", "4883EC20"), MemoryLayout.X64);

        var result = CodePatch.CreatePlan(memory, _entry, CreateObjectLayout(6));

        Assert.That(result.Error!.Message, Does.Contain("room"));
    }

    [Test]
    public void TryReadCall_AnEvenSequenceEitherSideOfThePayload_AcceptsTheRead()
    {
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];

        StandingPatch standing = new();
        standing.WriteSequence(4);
        standing.WritePayload(payload);

        var patch = StandingPatch.Installed();

        Span<byte> destination = stackalloc byte[8];
        bool read = patch.TryReadCall(standing.Memory, destination).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.True);
            Assert.That(destination.ToArray(), Is.EqualTo(payload));
            Assert.That(standing.SequenceReads, Is.EqualTo(2));
        }
    }

    [Test]
    public void TryReadCall_AnOddSequence_AsksAgainRatherThanReading()
    {
        byte[] payload = [0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7];

        StandingPatch standing = new();
        standing.WritePayload(payload);

        // Makes the sequence odd on the first read, as if the stub were midway through writing the payload. Makes it even
        // and stable from then on.
        standing.BeforeSequenceRead = reads => standing.WriteSequence(reads == 0 ? 3ul : 4ul);

        var patch = StandingPatch.Installed();

        Span<byte> destination = stackalloc byte[8];
        bool read = patch.TryReadCall(standing.Memory, destination).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.True);
            Assert.That(destination.ToArray(), Is.EqualTo(payload));

            // Expects the odd read, then the 2 reads that agreed.
            Assert.That(standing.SequenceReads, Is.EqualTo(3));
        }
    }

    [Test]
    public void TryReadCall_ATargetThatNeverStopsWriting_SaysSoRatherThanHandingBackHalfAPayload()
    {
        StandingPatch standing = new();
        standing.WriteSequence(3);

        var patch = StandingPatch.Installed();

        Span<byte> destination = stackalloc byte[8];
        bool read = patch.TryReadCall(standing.Memory, destination).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.False);
            Assert.That(standing.SequenceReads, Is.EqualTo(8));
        }
    }

    // A call that arrives between the 2 reads isn't accepted, since the reads come back even and different and the payload
    // can't be attributed to either call.
    [Test]
    public void TryReadCall_ACallLandingBetweenTheTwoReads_DoesNotAcceptIt()
    {
        StandingPatch standing = new();

        standing.BeforeSequenceRead = reads => standing.WriteSequence((ulong)(reads * 2));

        var patch = StandingPatch.Installed();

        Span<byte> destination = stackalloc byte[8];
        bool read = patch.TryReadCall(standing.Memory, destination).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.False);
            Assert.That(standing.SequenceReads, Is.EqualTo(16));
        }
    }

    [Test]
    public void TryReadCall_AReadWiderThanTheRecordsPayload_RefusesIt()
    {
        StandingPatch standing = new();
        var patch = StandingPatch.Installed();

        Assert.That(
            () => patch.TryReadCall(standing.Memory, new byte[CaptureLayout.PayloadSize + 1]),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // An adopted patch can't be read, because its page doesn't record which layout its writer used.
    [Test]
    public void TryReadCall_AnAdoptedPatch_ReportsTheLayoutAsUnknown()
    {
        StandingPatch standing = new();
        standing.WriteSequence(4);

        var patch = standing.Adopt();

        var result = patch.TryReadCall(standing.Memory, new byte[8]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.IsAdopted, Is.True);
            Assert.That(patch.HasCaptures, Is.False);
            Assert.That(result.Error!.Message, Does.Contain("isn't known"));
            Assert.That(standing.SequenceReads, Is.Zero);
        }
    }

    // A patch installed with an empty layout reports that it captures nothing instead of returning zeroes.
    [Test]
    public void TryReadCall_APatchThatOnlyCounts_ReportsThatItCapturesNothing()
    {
        StandingPatch standing = new();
        CodePatch patch = new(new Address(0x20000), new Address(0x20040), new Address(0x20200), new byte[5], adopted: false, captures: false);

        var result = patch.TryReadCall(standing.Memory, new byte[8]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error!.Message, Does.Contain("captures nothing"));
            Assert.That(standing.SequenceReads, Is.Zero);
        }
    }

    // A page composed for an entry reads back as the patch for that entry.
    [Test]
    public void Adopt_AComposedPage_ReadsItBack()
    {
        Address entry = new(0x20000);
        var slot = entry + 0x40;
        var record = entry + 0x200;
        byte[] bytes = new byte[0x200 + CaptureLayout.RecordSize];

        byte[] page = CodePatch.Assemble(entry, slot, record, _prologue, CaptureLayout.None, StubAssembler.X64).Unwrap();
        page.CopyTo(bytes, 0x40);
        bytes[0] = 0xE9;
        BitConverter.GetBytes((int)(slot + 0x30 - (entry + 5))).CopyTo(bytes, 1);

        var patch = CodePatch.Adopt(new BufferMemory(entry, bytes, MemoryLayout.X64), entry).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Entry, Is.EqualTo(entry));
            Assert.That(patch.Slot, Is.EqualTo(slot));
            Assert.That(patch.Record, Is.EqualTo(record));
            Assert.That(patch.OriginalBytes, Is.EqualTo(_prologue));
            Assert.That(patch.IsAdopted, Is.True);
        }
    }

    // A 32-bit jump wraps at 4 GB, so a page above 2 GB is found from an entry below it.
    [Test]
    public void Adopt_A32BitPageAboveTwoGigabytes_ReadsItBack()
    {
        Address entry = new(0x00401000);
        Address slot = new(0xC0000000);
        Address record = new(0xBFFF0000);
        byte[] page = CodePatch.Assemble(entry, slot, record, _prologue32, CaptureLayout.None, StubAssembler.X86).Unwrap();

        SparseMemory memory = new(MemoryLayout.X86);
        memory.Add(slot, page);
        memory.Add(entry, [0xE9, .. BitConverter.GetBytes(unchecked((int)(uint)(slot.Value + 0x30 - (entry.Value + 5))))]);

        var patch = CodePatch.Adopt(memory, entry).Unwrap();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(patch.Slot, Is.EqualTo(slot));
            Assert.That(patch.Record, Is.EqualTo(record));
        }
    }

    // A page whose patch length isn't the 5-byte jump isn't adopted.
    [Test]
    public void Adopt_APageWithAnotherPatchLength_Fails()
    {
        Address entry = new(0x20000);
        var slot = entry + 0x40;
        byte[] bytes = new byte[0x200 + CaptureLayout.RecordSize];

        byte[] page = CodePatch.Assemble(entry, slot, entry + 0x200, _prologue, CaptureLayout.None, StubAssembler.X64).Unwrap();
        page[0x0E] = 14;
        page.CopyTo(bytes, 0x40);
        bytes[0] = 0xE9;
        BitConverter.GetBytes((int)(slot + 0x30 - (entry + 5))).CopyTo(bytes, 1);

        var result = CodePatch.Adopt(new BufferMemory(entry, bytes, MemoryLayout.X64), entry);

        Assert.That(result.Error!.Message, Does.Contain("14"));
    }

    // An unreadable entry fails with an adoption error.
    [Test]
    public void Adopt_AnUnreadableEntry_Fails()
    {
        var result = CodePatch.Adopt(new BufferMemory(new Address(0x20000), new byte[2], MemoryLayout.X64), new Address(0x20000));

        Assert.That(result.Error!.GetType().Name, Is.EqualTo("AdoptError"));
    }

    // A target that isn't x86 or x64 isn't adopted.
    [Test]
    public void Adopt_ANonIntelTarget_Fails()
    {
        var result = CodePatch.Adopt(new BufferMemory(new Address(0x20000), new byte[0x200], new MemoryLayout(2, ByteOrder.LittleEndian)), new Address(0x20000));

        Assert.That(result.Error!.Message, Does.Contain("x86 and x64"));
    }

    // An entry that doesn't start with a jump into one of this library's pages isn't adopted.
    [TestCase("4883EC2890", "isn't redirected")]
    [TestCase("E900000000", "other than this library")]
    public void Adopt_AnEntryWithoutOurPatch_Fails(string hex, string reason)
    {
        Address entry = new(0x20000);
        byte[] bytes = new byte[0x200];
        FromHex(hex).CopyTo(bytes, 0);

        var result = CodePatch.Adopt(new BufferMemory(entry, bytes, MemoryLayout.X64), entry);

        Assert.That(result.Error!.Message, Does.Contain(reason));
    }

    // Maps separate ranges, because a 32-bit entry and a page above 2 GB are too far apart for 1 buffer.
    private sealed class SparseMemory(
        MemoryLayout layout) : IMemoryReader
    {
        private readonly List<(Address Start, byte[] Bytes)> _ranges = [];

        public MemoryLayout Layout => layout;

        public void Add(Address start, byte[] bytes)
        {
            byte[] padded = new byte[bytes.Length + 32];
            bytes.CopyTo(padded, 0);
            _ranges.Add((start, padded));
        }

        public Result Read(Address address, Span<byte> buffer)
        {
            foreach (var (start, bytes) in _ranges)
            {
                if (address >= start && address - start + buffer.Length <= bytes.Length)
                {
                    bytes.AsSpan((int)(address - start), buffer.Length).CopyTo(buffer);
                    return Result.Ok();
                }
            }

            return Errors.MemoryError.Unreadable(address, buffer.Length);
        }
    }

    private static BufferMemory CreateMemory(Address at, byte[] bytes, MemoryLayout layout)
    {
        byte[] padded = new byte[bytes.Length + 32];
        bytes.CopyTo(padded, 0);

        return new(at, padded, layout);
    }

    private static byte[] FromHex(params string[] hex)
    {
        string joined = string.Concat(hex);
        byte[] bytes = new byte[joined.Length / 2];

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(joined.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}
