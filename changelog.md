# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `GetMainModule` on `IMemoryModuleProvider` and `RemoteProcessMemory` for the module of the target's executable.
- `MemoryModule.GetFileHash` for the SHA256 of the module file. Each `MemoryModule` reads the file once.
- `Scan.ScanRegions` with a `MemoryRange` for scanning only the readable regions inside it.
- `Watcher.Error` for the error of the latest read, so a failed read under `FailAction.Clear` or `KeepOld` still shows why it failed.
- `CaptureLayout`, `CaptureItem` and `CaptureSource` in `AslHelp.Memory.Patching` for the arguments a patched entry copies out of each call.
- `CodePatch.Adopt`, `ReadCount` and `TryReadCall` for finding a patch at an entry and reading how often it ran and what the last call passed.
- `IPatchInstaller` in `AslHelp.Memory.Patching` for what redirects an entry into a stub and keeps the target still while several watchers ask at once.
- `CodePatch.Install` for writing a planned patch's stub into a `PatchPool` slot and redirecting the entry with 1 aligned 8-byte write while the target's threads are suspended, with `Restore` for putting the displaced bytes back the same way.
- `IPatchWriter` and `IThreadSuspension` in `AslHelp.Memory.Patching` for the writes and thread suspension a `PatchSession` needs from a process, so a package can supply them for its platform.
- `PatchSession` in `AslHelp.Memory.Patching`, the `IPatchInstaller` that keeps the ledger of 1 run. It adopts a patch already at an entry, returns the standing patch for a repeated entry, shares a `PatchPool` between entries in its reach and on `Dispose` puts every entry back before releasing each pool no thread stands in.
- An optional `Logger` for `RemoteProcessMemory`, which logs failed reads and writes at trace level, refused handles and module snapshots at debug level and a page that stays writable after a write as a warning.
- `RemotePatchWriter` in `AslHelp.Memory.Remote`, the `IPatchWriter` for a running Windows process. It reserves memory within a relative jump of an anchor, writes code with each page's own protection put back and suspends the target's threads while a patch is written.

### Changed
- **Breaking:** `IMemoryModuleProvider` has the new member `GetMainModule`, so an implementation needs it too.
- **Breaking:** `Scan.For` is now `Scan.Create`.
- **Breaking:** `ScanWindow.For` is now `ScanWindow.Create`.
- **Breaking:** `Scan.Forward` is now `ScanWindow.Forward`.
- **Breaking:** `Scan.Backward` is now `ScanWindow.Backward`.
- **Breaking:** `Scan.Range` is now `Scan.ScanRange`.
- **Breaking:** `Scan.Ranges` is now `Scan.ScanRanges`.
- **Breaking:** `Scan.Regions` is now `Scan.ScanRegions`.
- **Breaking:** `ScanStep.OnFound` is now `ScanStep.WithResolver`.
- **Breaking:** `AddressResolver.Relative` is now `AddressResolver.FromRelative`.
- **Breaking:** `AddressResolver.Displacement` is now `AddressResolver.FromDisplacement`.
- **Breaking:** `AddressResolver.Offset` is now `AddressResolver.FromOffset`.
- **Breaking:** `GlobalSlot` is now `GlobalPointer`.
- **Breaking:** `ReadKind.Branch` is now `Displacement`.
- **Breaking:** A `branch` read in a route document is now `displacement`.
- **Breaking:** `SearchStep.Search` is now `Scan`.
- The errors for a missing string terminator, scan pattern, pool slot and export table now read `doesn't have a` instead of `has no`.

### Fixed
- `RemoteProcessMemory` opens a process with the rights to read only and asks for the rights to write on the 1st write. A process that refuses writes can still be read.
- `RemoteProcessMemory` returns an error after `Dispose` instead of throwing `ObjectDisposedException`. A watcher that reads while the game exits gets that error.
- `RemoteProcessMemory.GetRegions` fails when Windows refuses a region query instead of returning the regions up to that address.
- `RemoteProcessMemory.GetModules` gives a 32-bit process the SysWOW64 paths of its system modules, which are the files `GetFileHash` and `GetFileVersion` need to read.
- `RemoteProcessMemory.GetMainModule`, and `GetModules` when Windows refuses the module snapshot, take the name of a module from its path when Windows refuses its base name, instead of leaving the module out.
- `RemoteProcessMemory.Write` makes a read-only or code page writable when Windows refuses the write and puts its protection back after it. A write Windows still refuses keeps the system message in its error.
- A failed read of `RemoteProcessMemory` keeps the system message and the number of bytes Windows read in its error. `Watcher.Error` shows both.
- `MergeReadable` and `Scan.ScanRegions` count an execute-only `PAGE_EXECUTE` region as readable.

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
