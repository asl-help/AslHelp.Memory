using AslHelp.Memory;
using AslHelp.Memory.Patching;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Patching;

// Stands in for a suspension of a live target. A test sets where each thread stopped and whether every thread
// answered, so no process is suspended.
internal sealed class FakeThreadSuspension : IThreadSuspension
{
    public List<Address> Positions { get; } = [];

    public bool IsComplete { get; set; } = true;

    public bool Disposed { get; private set; }

    public bool AnyIn(MemoryRange range)
    {
        foreach (var position in Positions)
        {
            // Skips the first byte, because a thread stopped there has not read the bytes yet.
            if (range.Contains(position) && position != range.Start)
            {
                return true;
            }
        }

        return false;
    }

    public bool Any(Predicate<Address> match)
    {
        foreach (var position in Positions)
        {
            if (match(position))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        Disposed = true;
    }
}
