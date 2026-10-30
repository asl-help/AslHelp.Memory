using AslHelp.Memory.Errors;
using AslHelp.Memory.Patching;
using System;

namespace AslHelp.Memory.Tests.Patching;

public partial class CodePatchTests
{
    // Lays out an entry redirected into a slot whose header points at the record. Adopting that entry gives the read
    // protocol a patch to read without installing one.
    private sealed class StandingPatch
    {
        private static readonly Address _base = new(0x20000);
        private const int SlotAt = 0x40;
        private const int RecordAt = 0x200;

        private const int SequenceAt = 0x08;

        private readonly byte[] _bytes = new byte[RecordAt + CaptureLayout.RecordSize];

        public StandingPatch()
        {
            var entry = _base;
            var record = _base + RecordAt;

            _bytes[0] = 0xE9;
            BitConverter.GetBytes(SlotAt + 0x30 - 5).CopyTo(_bytes, 1);

            BitConverter.GetBytes(record.Value).CopyTo(_bytes, SlotAt + 0x00);
            BitConverter.GetBytes(0x504C5341).CopyTo(_bytes, SlotAt + 0x08);
            BitConverter.GetBytes((ushort)2).CopyTo(_bytes, SlotAt + 0x0C);
            _bytes[SlotAt + 0x0E] = 5;
            _bytes[SlotAt + 0x0F] = 5;
            BitConverter.GetBytes(entry.Value).CopyTo(_bytes, SlotAt + 0x10);

            Memory = new SequenceWatchingMemory(this, _base, _bytes);
        }

        public IMemoryReader Memory { get; }

        public int SequenceReads { get; private set; }

        // Runs against the record before each read of the sequence, so a test can stage a call that arrives mid read.
        public Action<int>? BeforeSequenceRead { get; set; }

        // Returns the patch as seen by the run that installed it. A patch found already in place keeps whatever its
        // writer captured. The page doesn't record what, so only an installed patch has a payload worth reading.
        public static CodePatch Installed()
        {
            return new(_base, _base + SlotAt, _base + RecordAt, new byte[5], adopted: false, captures: true);
        }

        public CodePatch Adopt()
        {
            return CodePatch.Adopt(Memory, _base).Unwrap();
        }

        public void WriteSequence(ulong sequence)
        {
            BitConverter.GetBytes(sequence).CopyTo(_bytes, RecordAt + SequenceAt);
        }

        public void WritePayload(byte[] payload)
        {
            payload.CopyTo(_bytes, RecordAt + CaptureLayout.PayloadOffset);
        }

        private void Reading(Address address)
        {
            if (address != _base + RecordAt + SequenceAt)
            {
                return;
            }

            BeforeSequenceRead?.Invoke(SequenceReads);
            SequenceReads++;
        }

        private sealed class SequenceWatchingMemory(
            StandingPatch owner,
            Address @base,
            byte[] bytes) : IMemoryReader
        {
            public MemoryLayout Layout => MemoryLayout.X64;

            public Result Read(Address address, Span<byte> buffer)
            {
                owner.Reading(address);

                long offset = address - @base;

                if (offset < 0 || offset + buffer.Length > bytes.Length)
                {
                    return MemoryError.Unreadable(address, buffer.Length);
                }

                bytes.AsSpan((int)offset, buffer.Length).CopyTo(buffer);

                return Result.Ok();
            }
        }
    }
}
