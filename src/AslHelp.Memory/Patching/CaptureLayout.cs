using AslHelp.Memory.Patching.Errors;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Describes where a call's arguments are when a patched entry is reached and where in the record each one is
///     written.
/// </summary>
/// <remarks>
///     This describes the calling convention and nothing above it. Which slot has the instance, whether a hidden
///     return buffer displaces it and what a parameter's type means are settled by whoever knows the runtime, so only
///     positions are left here. An empty layout is the patch that only counts, which is what a method whose signature
///     cannot be described is given.
/// </remarks>
public sealed class CaptureLayout
{
    /// <summary>
    ///     The first offset in a record a capture may be written to. Below it the header has the count, the
    ///     sequence a reader checks and a reserved word.
    /// </summary>
    /// <remarks>
    ///     The reserved word is empty and stays reserved rather than being reclaimed, because a record is read by
    ///     whichever version of this library finds it. Moving a field means a patch left behind by a crash stops
    ///     being readable.
    /// </remarks>
    public const int PayloadAt = 0x18;

    /// <summary>
    ///     How many bytes of a record a capture may use.
    /// </summary>
    public const int PayloadSize = RecordSize - PayloadAt;

    // Fits the header and the payload of 1 call, so 2 records never share a cache line with a 64-byte line size.
    internal const int RecordSize = 0x80;

    /// <summary>
    ///     Gets the layout that captures nothing, leaving a patch that only counts.
    /// </summary>
    public static CaptureLayout None { get; } = new([]);

    /// <summary>
    ///     Initializes a new instance of the <see cref="CaptureLayout"/> class.
    /// </summary>
    /// <param name="items">What to capture, in no particular order.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="items"/> is <see langword="null"/>.
    /// </exception>
    public CaptureLayout(IReadOnlyList<CaptureItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = items;
    }

    /// <summary>
    ///     Gets what this layout captures.
    /// </summary>
    public IReadOnlyList<CaptureItem> Items { get; }

    /// <summary>
    ///     Gets a value indicating whether this layout captures nothing.
    /// </summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    ///     Writes the instructions that copy every captured value into the record.
    /// </summary>
    /// <param name="code">The stub being written, standing where it will in the target.</param>
    /// <param name="record">Where the record begins in the target.</param>
    /// <param name="assembler">What encodes the stub for the target.</param>
    /// <returns>
    ///     A successful result when every item was written, otherwise a failed result for the item that couldn't be.
    /// </returns>
    internal Result Emit(CodeWriter code, Address record, IStubAssembler assembler)
    {
        if (Placement(assembler.PointerSize).TryUnwrapErr(out var placementError))
        {
            return placementError;
        }

        foreach (var item in Items)
        {
            var target = record + item.RecordOffset;

            var written = item.Source switch
            {
                CaptureSource.Register => assembler.CaptureRegister(code, target, item.Index, item.Width, item.Indirect),
                CaptureSource.FloatRegister => assembler.CaptureFloatRegister(code, target, item.Index, item.Width, item.Indirect),
                CaptureSource.Stack => assembler.CaptureStack(code, target, item.Index, item.Width, item.Indirect),
                _ => CaptureError.UnknownCaptureSource(item.Source),
            };

            if (written.TryUnwrapErr(out var error))
            {
                return error;
            }
        }

        return Result.Ok();
    }

    // Checks overlaps over the whole layout before a byte is emitted, since an overlap is a property of the set, not of
    // one item.
    private Result Placement(int pointerWidth)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            var item = Items[i];

            if (item.Width is not (1 or 2 or 4 or 8))
            {
                return CaptureError.UnstorableWidth(item.Width);
            }

            if (item.Width > pointerWidth)
            {
                return CaptureError.WidthBeyondPointer(item.Width, pointerWidth);
            }

            if (item.RecordOffset < PayloadAt || item.RecordOffset + item.Width > RecordSize)
            {
                return CaptureError.OutsidePayload(item.Width, item.RecordOffset);
            }

            for (int j = 0; j < i; j++)
            {
                var earlier = Items[j];

                if (item.RecordOffset < earlier.RecordOffset + earlier.Width
                    && earlier.RecordOffset < item.RecordOffset + item.Width)
                {
                    return CaptureError.OverlappingCaptures(earlier.RecordOffset, item.RecordOffset);
                }
            }
        }

        return Result.Ok();
    }
}
