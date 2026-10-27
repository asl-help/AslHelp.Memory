using System;

namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents the write side of a target: pages it may execute, the bytes that reach them, and a suspension of
///     its threads while they are written.
/// </summary>
/// <remarks>
///     Changing page protection, writing bytes and stopping threads are separate calls on the target, so they are
///     separate members here. A reservation has to be within a relative jump of its anchor, so <see cref="Reserve"/>
///     takes an anchor rather than any free page. Anything that provides these can stand in for the live writer,
///     including a recording writer in a test.
/// </remarks>
internal interface IPatchWriter
{
    /// <summary>
    ///     Reserves memory the target may execute, close enough to <paramref name="anchor"/> that a relative jump
    ///     reaches it.
    /// </summary>
    /// <remarks>
    ///     How far the reservation falls matters: memory 2 gigabytes away needs a patch longer than 5 bytes, which
    ///     displaces more of the target's code. The reservation is readable and executable but not writable.
    ///     <see cref="WriteCode"/> lifts the protection for each write and puts it back.
    /// </remarks>
    /// <param name="anchor">The address the reservation must be reachable from.</param>
    /// <param name="size">How many bytes are wanted.</param>
    /// <returns>
    ///     The reservation's address on success, otherwise a failed result.
    /// </returns>
    Result<Address> Reserve(Address anchor, int size);

    /// <summary>
    ///     Sets the protection of part of a reservation to <paramref name="protection"/>.
    /// </summary>
    /// <param name="address">The first byte to change.</param>
    /// <param name="size">How many bytes to change.</param>
    /// <param name="protection">The access the bytes allow afterward.</param>
    /// <returns>
    ///     A successful result when the protection was changed, otherwise a failed result.
    /// </returns>
    Result Protect(Address address, int size, MemoryProtection protection);

    /// <summary>
    ///     Writes <paramref name="buffer"/> at <paramref name="address"/>, making the range writable for as long as
    ///     that takes and putting the protection back after.
    /// </summary>
    /// <param name="address">Where to write.</param>
    /// <param name="buffer">What to write.</param>
    /// <returns>
    ///     A successful result when every byte was written, otherwise a failed result.
    /// </returns>
    Result WriteCode(Address address, ReadOnlySpan<byte> buffer);

    /// <summary>
    ///     Releases a reservation taken by <see cref="Reserve"/>.
    /// </summary>
    /// <param name="reservation">The reservation's address.</param>
    /// <returns>
    ///     A successful result when the reservation was released, otherwise a failed result.
    /// </returns>
    Result Release(Address reservation);

    /// <summary>
    ///     Suspends every thread of the target, so its code can be written without one running through it.
    /// </summary>
    /// <returns>
    ///     The suspension on success, otherwise a failed result. Disposing it resumes what it suspended.
    /// </returns>
    Result<IThreadSuspension> SuspendThreads();
}
