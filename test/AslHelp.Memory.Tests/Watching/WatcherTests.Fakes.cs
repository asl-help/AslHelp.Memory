using AslHelp.Memory.Errors;
using AslHelp.Memory.Watching;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Watching;

public partial class WatcherTests
{
    private sealed class CountingMemory(
        IMemoryReader inner) : IMemoryReader
    {
        public int Reads { get; private set; }

        public MemoryLayout Layout => inner.Layout;

        public Result Read(Address address, Span<byte> buffer)
        {
            Reads++;
            return inner.Read(address, buffer);
        }
    }

    private sealed class SwitchableMemory(
        IMemoryReader inner) : IMemoryReader
    {
        public bool Fail { get; set; }

        public MemoryLayout Layout => inner.Layout;

        public Result Read(Address address, Span<byte> buffer)
        {
            return Fail ? new UnreadableError(address, buffer.Length) : inner.Read(address, buffer);
        }
    }

    // Throws until MakeReady runs, and then reads 1.
    private sealed class ReadyLater(
        ITickCounter ticks) : Watcher<int>(ticks)
    {
        private bool _isReady;

        public InvalidOperationException NotReady { get; } = new("The value isn't ready.");

        public void MakeReady()
        {
            _isReady = true;
            Invalidate();
        }

        protected override Result<int> Read()
        {
            return _isReady ? 1 : throw NotReady;
        }
    }

    // Reads whatever Value is set to, compared with the comparer it gets.
    private sealed class SettableWatcher(
        ITickCounter ticks,
        IEqualityComparer<int> comparer) : Watcher<int>(ticks, comparer)
    {
        public int Value { get; set; }

        protected override Result<int> Read()
        {
            return Value;
        }
    }

    // Treats 2 values as equal when they're at most 1 apart.
    private sealed class WithinOne : IEqualityComparer<int>
    {
        public bool Equals(int x, int y)
        {
            return Math.Abs(x - y) <= 1;
        }

        public int GetHashCode(int obj)
        {
            return 0;
        }
    }
}
