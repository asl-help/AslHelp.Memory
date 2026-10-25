using AslHelp.Memory.Patching.Errors;
using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents 1 method entry redirected into a counting stub and the page with that stub.
/// </summary>
/// <remarks>
///     <para>
///         The page describes itself: a marker, the entry it claims and the bytes it displaced. A run that crashed
///         leaves a page a later run can recognize, adopt and restore. What stays fragile is the write window, since
///         Windows does not resume a thread because whoever suspended it died, so that window has only a read and a
///         write.
///     </para>
///     <para>
///         Only the entry is ever patched: arithmetic flags are dead at a function's first instruction and live at a
///         large minority of interior sites.
///     </para>
/// </remarks>
public sealed class CodePatch
{
    private const uint Marker = 0x504C5341;
    private const ushort Version = 2;

    private const int RecordPointerAt = 0x00;
    private const int MarkerAt = 0x08;
    private const int VersionAt = 0x0C;
    private const int PatchLengthAt = 0x0E;
    private const int StolenLengthAt = 0x0F;
    private const int EntryAt = 0x10;
    private const int OriginalAt = 0x18;
    private const int StubAt = 0x30;

    // The record's header words. The payload begins past them at `CaptureLayout.PayloadAt`.
    private const int CountAt = 0x00;
    private const int SequenceAt = 0x08;

    private const int MaxStolen = StubAt - OriginalAt;
    private const int JumpLength = 5;

    // Fits the header, the displaced bytes and the longest stub a layout composes.
    internal const int SlotSize = 0xC0;

    internal CodePatch(Address entry, Address slot, Address record, byte[] original, bool adopted, bool captures)
    {
        Entry = entry;
        Slot = slot;
        Record = record;
        Original = original;
        WasAdopted = adopted;
        Captures = captures;
    }

    /// <summary>
    ///     Gets the entry that was patched.
    /// </summary>
    public Address Entry { get; }

    /// <summary>
    ///     Gets the arena slot with this patch's header and stub.
    /// </summary>
    public Address Slot { get; }

    /// <summary>
    ///     Gets the record the stub counts and captures into, which lives on a page no stub shares.
    /// </summary>
    public Address Record { get; }

    /// <summary>
    ///     Gets a value indicating whether this patch was found already installed rather than written by this session.
    /// </summary>
    /// <remarks>
    ///     An adopted patch belongs to whoever wrote it, which may be a run of this library that crashed. Releasing
    ///     the memory under it would take a live target down, so a caller that cleans up has to know which it
    ///     has.
    /// </remarks>
    public bool WasAdopted { get; }

    /// <summary>
    ///     Gets a value indicating whether the stub standing at this entry is one this run wrote a capture into.
    /// </summary>
    /// <remarks>
    ///     A patch that only counts leaves its record's payload as it found it, which is zeroes. Zeroes read as a
    ///     whole capture of nothing rather than as an absent one. An adopted patch is the same answer for a different
    ///     reason: it captures whatever its own writer asked for. The page doesn't record what that was.
    /// </remarks>
    public bool Captures { get; }

    /// <summary>
    ///     Gets the bytes the jump displaced.
    /// </summary>
    internal byte[] Original { get; }

    /// <summary>
    ///     Recognizes a patch this library already installed at <paramref name="entry"/>.
    /// </summary>
    /// <remarks>
    ///     A live patch is adopted rather than replaced. The target is already running correctly with it, so
    ///     un-patching to patch again is 2 more writes into a game for no gain. It also opens a window where the entry
    ///     is neither one thing nor the other.
    /// </remarks>
    /// <param name="memory">The target's memory.</param>
    /// <param name="entry">The method entry to inspect.</param>
    /// <returns>
    ///     The patch when one of ours is there, otherwise a failed result with what was found instead.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/> is <see langword="null"/>.
    /// </exception>
    public static Result<CodePatch> Adopt(IMemoryReader memory, Address entry)
    {
        ArgumentNullException.ThrowIfNull(memory);

        if (!memory.ReadArray<byte>(entry, JumpLength).TryUnwrap(out byte[]? head))
        {
            return PlanError.UnreadableEntry(entry);
        }

        if (head[0] != 0xE9)
        {
            return AdoptError.NotRedirected();
        }

        int displacement = BitConverter.ToInt32(head, 1);
        var slot = entry + JumpLength + displacement - StubAt;

        if (!memory.ReadArray<byte>(slot, StubAt).TryUnwrap(out byte[]? header)
            || BitConverter.ToUInt32(header, MarkerAt) != Marker)
        {
            return AdoptError.NotOurs();
        }

        if (BitConverter.ToUInt16(header, VersionAt) != Version)
        {
            return AdoptError.AnotherVersion();
        }

        if ((Address)BitConverter.ToUInt64(header, EntryAt) != entry)
        {
            return AdoptError.ClaimsAnotherEntry();
        }

        int stolen = header[StolenLengthAt];

        if (stolen is < JumpLength or > MaxStolen)
        {
            return AdoptError.ImplausibleStolenLength(stolen);
        }

        byte[] original = new byte[stolen];
        Buffer.BlockCopy(header, OriginalAt, original, 0, stolen);

        var record = (Address)BitConverter.ToUInt64(header, RecordPointerAt);

        return new CodePatch(entry, slot, record, original, adopted: true, captures: false);
    }
    /// <summary>
    ///     Answers whether a stub copying <paramref name="layout"/> fits the slot it is written into.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A slot is a fixed size and the stub shares it with the instructions the jump displaced, so what a stub
    ///         can copy is bounded. A caller composing a layout is the only one in a position to leave something
    ///         out. The check applies to any entry rather than one, since it runs while a layout is being built: the
    ///         longest run of instructions a patch ever displaces is assumed, so a yes here is true for every entry.
    ///     </para>
    ///     <para>
    ///         It composes a stub and measures it, so the answer is the emitter's own rather than a second count that
    ///         could drift from it.
    ///     </para>
    /// </remarks>
    /// <param name="layout">What the stub would copy.</param>
    /// <param name="assembler">What encodes the stub for the target.</param>
    /// <returns>
    ///     Whether a stub copying it fits.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="layout"/> is <see langword="null"/>.
    /// </exception>
    internal static bool Fits(CaptureLayout layout, IStubAssembler assembler)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return Compose(Address.Zero, Address.Zero, Address.Zero, new byte[MaxStolen], layout, assembler).IsOk;
    }

    /// <summary>
    ///     Reads how many times the patched entry has been reached.
    /// </summary>
    /// <param name="memory">The target's memory.</param>
    /// <returns>
    ///     The count on success, otherwise a failed result.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/> is <see langword="null"/>.
    /// </exception>
    public Result<ulong> ReadCount(IMemoryReader memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        return memory.Read<ulong>(Record + CountAt);
    }

    /// <summary>
    ///     Reads what the stub copied out of the call it last counted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The stub claims a sequence word before it writes and steps it on after, so the word is odd for exactly
    ///         as long as the payload is half written and even either side of it. This reads the sequence, the payload
    ///         and the sequence again as 3 ordered reads and takes the payload only when the 2 sequences are
    ///         equal and even, which is the 1 combination that shows nothing was written while it was being read. One
    ///         thread writes at a time: a call arriving while another is storing is counted and not captured.
    ///     </para>
    ///     <para>
    ///         Reading the payload and trusting the count beside it to say which call it came from does not work: the
    ///         target keeps running through the read, so one read is not one moment.
    ///     </para>
    ///     <para>
    ///         The count needs none of this: it's 1 aligned word that reads exactly at any rate. The stub counts a
    ///         call before capturing it, so a count read after this read includes the call the payload came from.
    ///     </para>
    /// </remarks>
    /// <param name="memory">The target's memory.</param>
    /// <param name="into">Where the payload goes, whose length is how much of it to read.</param>
    /// <returns>
    ///     <see langword="true"/> when a whole payload was caught between 2 calls, <see langword="false"/> when the
    ///     target was still writing on every attempt, otherwise a failed result, which is also what a patch nothing
    ///     captured into answers.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="into"/> is longer than a record's payload.
    /// </exception>
    public Result<bool> TryReadCall(IMemoryReader memory, Span<byte> into)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(into.Length, CaptureLayout.PayloadSize, nameof(into));

        // A record nothing captures into is all zeroes. Its sequence reads even and equal. The protocol below
        // would call that a whole capture of null.
        if (!Captures)
        {
            return AdoptError.CapturesNothing(Entry);
        }

        const int ReadAttempts = 8;
        for (int attempt = 0; attempt < ReadAttempts; attempt++)
        {
            if (!memory.Read<ulong>(Record + SequenceAt).TryUnwrap(out ulong before, out var beforeError))
            {
                return beforeError;
            }

            if ((before & 1) != 0)
            {
                continue;
            }

            if (memory.Read(Record + CaptureLayout.PayloadAt, into).TryUnwrapErr(out var payloadError))
            {
                return payloadError;
            }

            if (!memory.Read<ulong>(Record + SequenceAt).TryUnwrap(out ulong after, out var afterError))
            {
                return afterError;
            }

            if (after == before)
            {
                return true;
            }
        }

        return false;
    }
    /// <summary>
    ///     Settles everything about an entry that does not depend on where its stub will stand.
    /// </summary>
    /// <remarks>
    ///     Every refusal happens here, before an arena is reserved.
    /// </remarks>
    /// <param name="memory">The target's memory.</param>
    /// <param name="entry">The method entry to patch.</param>
    /// <param name="layout">What the stub copies out of the call, which may be nothing.</param>
    /// <returns>
    ///     What an install needs on success, otherwise a failed result with the reason the entry was refused.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="memory"/> or <paramref name="layout"/> is <see langword="null"/>.
    /// </exception>
    internal static Result<PatchPlan> Plan(IMemoryReader memory, Address entry, CaptureLayout layout)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(layout);

        // Requires the 5 bytes to sit inside 1 8-byte aligned word, so the installing write is a single store
        // the target cannot observe half of.
        if ((entry.Value & 7) > 8 - JumpLength)
        {
            return PlanError.StraddlesAWord(entry);
        }

        if (!memory.ReadArray<byte>(entry, MaxStolen).TryUnwrap(out byte[]? prologue))
        {
            return PlanError.UnreadableEntry(entry);
        }

        int pointerSize = memory.Layout.PointerSize;
        if (pointerSize is not (4 or 8))
        {
            return PlanError.NotIntel(pointerSize);
        }

        bool is64Bit = pointerSize == 8;

        if (!InstructionLength.TryStealLength(prologue, is64Bit, JumpLength, out int stolen, out bool relative))
        {
            return PlanError.UnrecognisedPrologue(entry);
        }

        if (relative)
        {
            return PlanError.PositionDependentEntry(entry);
        }

        byte[] original = new byte[stolen];
        Buffer.BlockCopy(prologue, 0, original, 0, stolen);

        // Composes a throwaway stub against the entry itself, because every displacement is 4 bytes wherever it points
        // and the place of the stub and its record changes no length.
        var assembler = StubAssembler.For(is64Bit);

        if (Compose(entry, entry, entry, original, layout, assembler).TryUnwrapErr(out var shapeError))
        {
            return shapeError;
        }

        return new PatchPlan(entry, original, layout, assembler);
    }

    internal static Result<byte[]> Compose(
        Address entry,
        Address slot,
        Address record,
        byte[] original,
        CaptureLayout layout,
        IStubAssembler assembler)
    {
        byte[] header = new byte[StubAt];

        BitConverter.GetBytes(record.Value).CopyTo(header, RecordPointerAt);
        BitConverter.GetBytes(Marker).CopyTo(header, MarkerAt);
        BitConverter.GetBytes(Version).CopyTo(header, VersionAt);
        header[PatchLengthAt] = JumpLength;
        header[StolenLengthAt] = (byte)original.Length;
        BitConverter.GetBytes(entry.Value).CopyTo(header, EntryAt);
        original.CopyTo(header, OriginalAt);

        CodeWriter code = new(slot + StubAt);

        // Counts the call before capturing it, so a payload the reader catches whole belongs to a call the count
        // already includes, whichever of the 2 the reader reads first.
        assembler.Increment(code, record + CountAt);

        // The stub claims the sequence word before storing and steps it on after (`TryReadCall` has the protocol). A
        // count alone would let 2 threads arriving together both store on an even word. The claim sends the second
        // thread past the stores to the resume.
        if (!layout.IsEmpty)
        {
            assembler.Claim(code, record + SequenceAt);

            int skip = assembler.SkipWhenClaimed(code);

            if (layout.Emit(code, record, assembler).TryUnwrapErr(out var emitError))
            {
                return emitError;
            }

            assembler.Increment(code, record + SequenceAt);
            assembler.ResolveSkip(code, skip);
        }

        assembler.Resume(code, original, entry + original.Length);

        if (StubAt + code.Length > SlotSize)
        {
            return PlanError.StubTooLarge(entry, code.Length, SlotSize - StubAt);
        }

        byte[] composed = [.. header, .. code.Written];

        return composed;
    }
}
