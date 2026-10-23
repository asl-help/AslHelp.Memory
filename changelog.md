# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `Address` for an address in a target's memory, with offset arithmetic, comparison and `0x` hex formatting.
- `MemoryRange` for a contiguous range of bytes with its `End` and a `Contains` check.
- `MemoryLayout` and `ByteOrder` for a target's pointer size from 1 to 8 bytes and its byte order, with the `X86`, `X64` and `BigEndian32` presets.
- `IMemoryReader` and `IMemoryWriter` for reading and writing raw bytes of a target's memory.
- `Read`, `ReadArray`, `ReadPointer` and `Deref` for reading values, pointers and pointer chains in the target's layout.
- `ReadString` and `ReadTerminatedString` for reading ASCII, UTF-8 and UTF-16 strings, with `StringType`.

[Unreleased]: https://github.com/asl-help/AslHelp.Memory/commits/main
