using AslHelp.Memory.Errors;
using AslHelp.Memory.Watching;
using System;

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
            return Fail ? MemoryError.Unreadable(address, buffer.Length) : inner.Read(address, buffer);
        }
    }

    // Throws until MakeReady runs, and then reads 1.
    private sealed class ReadyLater(
        ITickCounter ticks,
        IMemoryReader reader) : Watcher<int>(ticks, reader)
    {
        private bool _isReady;

        public void MakeReady()
        {
            _isReady = true;
            Invalidate();
        }

        protected override Result<int> Read(IMemoryReader reader)
        {
            return _isReady ? 1 : throw new InvalidOperationException("The value isn't ready.");
        }
    }
}
