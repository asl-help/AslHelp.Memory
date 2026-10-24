# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `Address` for an address in a target's memory, with offset arithmetic, distances, comparison, `0x` hex formatting and parsing.
- `MemoryRange` for a contiguous range of bytes with its `End` and a `Contains` check.
- `MemoryLayout` and `ByteOrder` for a target's pointer size from 1 to 8 bytes and its byte order, with the `X86`, `X64` and `BigEndian32` presets.
- `IMemoryReader` and `IMemoryWriter` for reading and writing raw bytes of a target's memory.
- `Read`, `ReadArray`, `ReadPointer` and `Deref` for reading values, pointers and pointer chains in the target's layout.
- `ReadString` for reading ASCII, UTF-8 and UTF-16 strings of a given length or up to their null terminator, with `StringType`.
- `IMemoryRegionProvider`, `MemoryRegion` and `MemoryProtection` for listing the committed regions of a target's memory with their access.
- `IMemoryModuleProvider` and `MemoryModule` for listing the modules loaded in a target, with `GetModule` for finding a module by name in any case.
- `BufferMemory` for reading and writing a byte array mapped at a base address, with `Range` for the bytes it covers.
- `ScanPattern` for parsing byte signatures such as `48 8B 05 ????????` with wildcard nibbles, with `Parse`, `TryParse`, `Length` and `ToString`.
- `Scan.Range`, `Scan.Ranges` and `Scan.Regions` for scanning ranges or every readable region for a pattern in chunks, skipping chunks that can't be read.
- `Scan.For`, `Scan.Forward`, `Scan.Backward`, `ScanStep` and `ScanWindow` for chained scans that search around the previous step's match.
- `AddressResolver` with `Relative`, `Displacement` and `Offset` for turning a match into the address an instruction references.
- `RemoteProcessMemory` in the new `AslHelp.Memory.Remote` package for reading, writing, regions and modules of another Windows process.

[Unreleased]: https://github.com/asl-help/AslHelp.Memory/commits/main
