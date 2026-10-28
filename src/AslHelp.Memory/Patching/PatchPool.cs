using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents 1 reservation in the target, shared by many patches.
/// </summary>
/// <remarks>
///     Windows reserves address space in 64 KB blocks whatever size is asked for, so 1 block serves hundreds of patches
///     rather than spending a neighborhood on each. Records sit on their own pages: a stub writing into a record on
///     the page it runs from is self-modifying code. A record merely a cache line away still costs 57 ns a call
///     against 2.
/// </remarks>
internal sealed class PatchPool
{
    private const int PageSize = 0x1000;
    private const int ReservationSize = 0x10000;

    // Records take whole pages and code takes the rest, so records and code never share a page. 6 pages balance
    // them. A 7th would fit more records than the remaining pages can serve stubs for.
    private const int RecordPages = 6;
    private const int CodeBase = RecordPages * PageSize;
    private const int SlotCount = CodeBase / CaptureLayout.RecordSize;
    private const MemoryProtection RecordProtection = MemoryProtection.Read | MemoryProtection.Write;

    private int _rented;

    private PatchPool(Address @base)
    {
        Base = @base;
    }

    /// <summary>
    ///     Gets the reservation's first address, which is where the records begin.
    /// </summary>
    public Address Base { get; }

    /// <summary>
    ///     Gets a value indicating whether every slot has been handed out.
    /// </summary>
    public bool IsFull => _rented == SlotCount;

    /// <summary>
    ///     Reserves a block near <paramref name="anchor"/> and prepares it to hand out slots.
    /// </summary>
    /// <param name="writer">The write access to the target.</param>
    /// <param name="anchor">The address every slot must be reachable from.</param>
    /// <returns>
    ///     The pool on success, otherwise a failed result.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="writer"/> is <see langword="null"/>.
    /// </exception>
    public static Result<PatchPool> Create(IPatchWriter writer, Address anchor)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!writer.Reserve(anchor, ReservationSize).TryUnwrap(out var @base, out var error))
        {
            return error;
        }

        if (writer.Protect(@base, CodeBase, RecordProtection).TryUnwrapErr(out var protectError))
        {
            _ = writer.Release(@base);

            return protectError;
        }

        return new PatchPool(@base);
    }

    /// <summary>
    ///     Determines whether a patch at <paramref name="entry"/> could jump into this pool.
    /// </summary>
    /// <param name="entry">The entry a patch would redirect.</param>
    /// <returns>
    ///     Whether every slot here is within a relative jump of it.
    /// </returns>
    public bool CanReach(Address entry)
    {
        long low = Base - entry;
        long high = Base + ReservationSize - entry;

        return low is > int.MinValue and < int.MaxValue
            && high is > int.MinValue and < int.MaxValue;
    }

    /// <summary>
    ///     Hands out the next free slot.
    /// </summary>
    /// <param name="record">The bytes on the record pages this slot counts and captures into.</param>
    /// <param name="slot">Where this slot's header and stub live.</param>
    /// <returns>
    ///     Whether a slot was free.
    /// </returns>
    public bool TryRent(out Address record, out Address slot)
    {
        if (IsFull)
        {
            record = Address.Zero;
            slot = Address.Zero;

            return false;
        }

        record = Base + (_rented * CaptureLayout.RecordSize);
        slot = Base + CodeBase + (_rented * CodePatch.SlotSize);
        _rented++;

        return true;
    }

    /// <summary>
    ///     Takes back the slot handed out last, for a caller that got no further than taking it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Takes back only the last slot, since slots are handed out by counting up and stepping the count back is
    ///         the one return that needs no bookkeeping.
    ///     </para>
    ///     <para>
    ///         A returned slot is safe to hand out again because nothing in the target can reach it: whatever was
    ///         written into it is unreachable until an entry jumps there. The next patch to take it writes its own
    ///         stub over the top and clears its record before that jump exists.
    ///     </para>
    /// </remarks>
    /// <param name="slot">The slot to give back.</param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="slot"/> is not the slot this pool handed out last.
    /// </exception>
    public void Return(Address slot)
    {
        if (_rented == 0 || slot != Base + CodeBase + ((_rented - 1) * CodePatch.SlotSize))
        {
            throw new ArgumentException(
                $"The slot at {slot} is not the one handed out last, which is the only one that can be given back.",
                nameof(slot));
        }

        _rented--;
    }

    /// <summary>
    ///     Determines whether <paramref name="address"/> lies inside this pool's reservation.
    /// </summary>
    /// <param name="address">The address to test.</param>
    /// <returns>
    ///     Whether the address lies in this reservation.
    /// </returns>
    public bool Contains(Address address)
    {
        // Measures the offset unsigned, so a base whose end wraps past the top still contains its bytes.
        return unchecked((ulong)(address - Base)) < ReservationSize;
    }
}
