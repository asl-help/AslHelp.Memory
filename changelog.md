# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `CaptureLayout`, `CaptureItem` and `CaptureSource` in `AslHelp.Memory.Patching` for the arguments a patched entry copies out of each call.
- `CodePatch.Adopt`, `ReadCount` and `TryReadCall` for finding a patch at an entry and reading how often it ran and what the last call passed.

## [0.1.0-alpha.1] - 2026-10-25

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
- `MergeReadable` for joining adjacent readable `MemoryRegion`s into contiguous `MemoryRange`s.
- `PeDirectory`, `PeDataDirectory` and `GetDataDirectory` in `AslHelp.Memory.Pe` for reading a loaded module's PE data directory through any `IMemoryReader`.
- `GetExport` for finding the address of a module's named export with a binary search over its sorted name table.
- `MemoryLocator` and `LocatorRoute` in `AslHelp.Memory.Locating` for named routes from a module's base to an address, tried until 1 of them locates it, with a JSON form. `Routes` looks a route up by name.
- `ExportStep`, `SearchStep`, `ReadStep` with `ReadKind` and `OffsetStep` for the steps of a route, each with an offset.
- `GlobalSlot` for reading the pointer in a located slot on each access, with a timed retry after a failed locate.
- `MemoryWatcher<T>`, `StringWatcher` and `ArrayWatcher<T>` in `AslHelp.Memory.Watching` for a value at the end of a pointer path, read at most once per tick of an `ITickCounter` and only when accessed, with `Old`, `Current`, `Changed`, `ChangedTo`, `ChangedFrom` and `Check`.
- `FailAction` for what a watcher does when a read fails: clear its values, keep them or throw with the error of the read.
- `RemoteProcessMemory` in the new `AslHelp.Memory.Remote` package for reading, writing, regions and modules of another Windows process.

[Unreleased]: https://github.com/asl-help/AslHelp.Memory/compare/0.1.0-alpha.1...HEAD
[0.1.0-alpha.1]: https://github.com/asl-help/AslHelp.Memory/releases/tag/0.1.0-alpha.1
