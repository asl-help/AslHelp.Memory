using AslHelp.Memory;
using AslHelp.Memory.Patching;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Patching;

// Stands in for the live writer so the pool and installs can be tested without a process. Every call records what
// it was asked and hands back a canned result, so a test reads the fields to check what the pool or install did.
internal sealed class FakePatchWriter : IPatchWriter
{
    // Starts high enough that a reservation and its last slot stay well clear of 0, so a test that does not care
    // about the top edge never trips over the wrap.
    public Address NextBase { get; set; } = new(0x1_0000_0000);

    public ResultError? ReserveError { get; set; }

    public ResultError? ProtectError { get; set; }

    public ResultError? ReleaseError { get; set; }

    // Fails every write when FailWriteAt is null, otherwise only the write at that address.
    public ResultError? WriteError { get; set; }

    public Address? FailWriteAt { get; set; }

    public ResultError? SuspendError { get; set; }

    // Receives every write when set, so a test can read the jump and the restored prologue back.
    public IMemoryWriter? Backing { get; set; }

    // Returned by SuspendThreads in place of a NoOpSuspension, so a test can place threads and mark the suspension
    // incomplete.
    public IThreadSuspension? Suspension { get; set; }

    // Runs inside SuspendThreads before it returns, so a test can change the target while its threads are stopped.
    public Action? OnSuspend { get; set; }

    public int Suspensions { get; private set; }

    public List<(Address Anchor, int Size)> Reserved { get; } = [];

    public List<(Address Address, int Size, MemoryProtection Protection)> ProtectionChanges { get; } = [];

    public List<Address> Released { get; } = [];

    public List<(Address Address, byte[] Bytes)> Written { get; } = [];

    // Builds an error a test can hand to one of the error fields and then match by reference on the way back.
    public static ResultError CreateError(string message = "test")
    {
        return new TestError(message);
    }

    public Result<Address> Reserve(Address anchor, int size)
    {
        Reserved.Add((anchor, size));

        if (ReserveError is not null)
        {
            return ReserveError;
        }

        return NextBase;
    }

    public Result Protect(Address address, int size, MemoryProtection protection)
    {
        ProtectionChanges.Add((address, size, protection));

        if (ProtectError is not null)
        {
            return ProtectError;
        }

        return Result.Ok();
    }

    public Result WriteCode(Address address, ReadOnlySpan<byte> buffer)
    {
        Written.Add((address, buffer.ToArray()));

        if (WriteError is not null && (FailWriteAt is null || FailWriteAt == address))
        {
            return WriteError;
        }

        if (Backing is not null)
        {
            return Backing.Write(address, buffer);
        }

        return Result.Ok();
    }

    public Result Release(Address reservation)
    {
        Released.Add(reservation);

        if (ReleaseError is not null)
        {
            return ReleaseError;
        }

        return Result.Ok();
    }

    public Result<IThreadSuspension> SuspendThreads()
    {
        Suspensions++;

        if (SuspendError is not null)
        {
            return SuspendError;
        }

        OnSuspend?.Invoke();

        return Result.Ok(Suspension ?? new NoOpSuspension());
    }

    private sealed record TestError(string Text) : ResultError
    {
        public override string Message => Text;
    }

    private sealed class NoOpSuspension : IThreadSuspension
    {
        public bool IsComplete => true;

        public bool AnyIn(MemoryRange range)
        {
            return false;
        }

        public bool Any(Predicate<Address> match)
        {
            return false;
        }

        public void Dispose()
        {
        }
    }
}
