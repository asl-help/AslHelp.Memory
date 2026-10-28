using AslHelp.Logging;
using AslHelp.Memory;
using AslHelp.Memory.Errors;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Tests.Patching;

// Builds the targets the session fixtures install into. A target is 1 BufferMemory over a pool's block and its entries
// or 2 of them out of each other's reach, each behind a FakePatchWriter backed by that memory.
internal static class SessionTargets
{
    public const int PoolSize = 0x10000;

    public static readonly Address Base = new(0x1_0000_0000);
    public static readonly Address Entry = Base + 0x20000;

    // Puts a 2nd entry far enough past the 1st that no pool reaches both.
    public const long FarOffset = 0x2_0000_0000;

    public static readonly Address Base32 = new(0x400000);
    public static readonly Address Entry32 = new(0x420000);

    // `sub rsp, 0x28` and a `nop`, 5 bytes displaced whole.
    public static readonly byte[] Prologue = [0x48, 0x83, 0xEC, 0x28, 0x90];

    // `push ebp`, `mov ebp, esp` and `sub esp, 8`, the MSVC prologue that displaces 6 bytes.
    public static readonly byte[] Prologue32 = [0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08];

    // Maps 1 array over the pool's block and `span` bytes from the entry, with the prologue at the entry and a writer
    // backed by it that reserves at `base`. A plan reads 0x18 bytes, so `span` covers the last entry plus that.
    public static (BufferMemory Memory, FakePatchWriter Writer) Target(
        MemoryLayout layout,
        Address @base,
        Address entry,
        byte[] prologue,
        int span = 0x40)
    {
        var low = entry < @base ? entry : @base;
        long size = Math.Max(@base - low + PoolSize, entry - low + Math.Max(span, 0x40));

        BufferMemory memory = new(low, new byte[size], layout);
        memory.Write(entry, prologue).Unwrap();

        FakePatchWriter writer = new() { NextBase = @base, Backing = memory };

        return (memory, writer);
    }

    // Maps the usual target and a 2nd range `FarOffset` past the entry, each with the prologue at its entry.
    public static (SplitMemory Memory, FakePatchWriter Writer, Address Far) FarTarget()
    {
        var far = Entry + FarOffset;

        BufferMemory near = new(Base, new byte[Entry - Base + 0x40], MemoryLayout.X64);
        near.Write(Entry, Prologue).Unwrap();

        BufferMemory distant = new(far - 0x20000, new byte[0x20040], MemoryLayout.X64);
        distant.Write(far, Prologue).Unwrap();

        SplitMemory memory = new(near, distant);
        FakePatchWriter writer = new() { NextBase = Base, Backing = memory };

        return (memory, writer, far);
    }

    // Begins the stub 0x30 bytes into its slot, after the header.
    public const int StubOffset = 0x30;

    // Builds the 5-byte jump from `entry` into the stub of the slot at `slot`.
    public static byte[] Jump(Address entry, Address slot)
    {
        int displacement = (int)(slot + StubOffset - (entry + 5));

        return [0xE9, .. BitConverter.GetBytes(displacement)];
    }

    public static byte[] ReadAt(BufferMemory memory, Address address, int length)
    {
        byte[] bytes = new byte[length];
        memory.Read(address, bytes).Unwrap();

        return bytes;
    }

    // Builds a logger that sends every line to a RecordingSink the test reads back.
    public static (Logger Logger, RecordingSink Sink) Logged()
    {
        RecordingSink sink = new();
        Logger logger = new();
        logger.Add(sink);

        return (logger, sink);
    }

    // Counts the warnings whose message contains `text`.
    public static int Warnings(RecordingSink sink, string text)
    {
        int count = 0;

        foreach (var (level, message) in sink.Lines)
        {
            if (level == LogLevel.Warning && message.Contains(text, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    // Maps 2 BufferMemory ranges as 1 memory, since 1 array can't span the 8 GB between 2 pools out of each other's
    // reach.
    public sealed class SplitMemory(
        BufferMemory first,
        BufferMemory second) : IMemoryReader, IMemoryWriter
    {
        public MemoryLayout Layout => first.Layout;

        public Result Read(Address address, Span<byte> buffer)
        {
            return first.Range.Contains(address) ? first.Read(address, buffer) : second.Read(address, buffer);
        }

        public Result Write(Address address, ReadOnlySpan<byte> buffer)
        {
            return first.Range.Contains(address) ? first.Write(address, buffer) : second.Write(address, buffer);
        }
    }

    // Fails every read of the entry's aligned word once the test switches failing on, because that word is what the
    // session reads to put the entry back.
    public sealed class EntryReadFailure(
        IMemoryReader inner,
        Address entry) : IMemoryReader
    {
        private readonly Address _word = new(entry.Value & ~7UL);

        public bool Fail { get; set; }

        public MemoryLayout Layout => inner.Layout;

        public Result Read(Address address, Span<byte> buffer)
        {
            return Fail && address == _word
                ? MemoryError.Unreadable(address, buffer.Length)
                : inner.Read(address, buffer);
        }
    }

    // Records the level and message of every line a logger emits.
    public sealed class RecordingSink : ILogSink
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Emit(in LogEvent e, int indent)
        {
            Lines.Add((e.Level, e.Message));
        }

        public void Dispose()
        {
        }
    }
}
