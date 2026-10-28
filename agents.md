# agents.md

## Commands

```
dotnet build
dotnet test
dotnet format --severity info --verify-no-changes
```

The `net4.8.1` tests only run on Windows.

## Rules

- Text files are UTF-8 without a BOM. Git stores them with LF line endings.
- `readme.md`, `changelog.md` and this file change in the same commit as the code they describe.
- Commit subjects are lowercase and imperative, with no body and no trailers.

## Samples

Each sample lives in `samples/<name>/` and is a single-file app whose entry point is `<name>.cs`, starting with `#!/usr/bin/env dotnet`. Its `expected.sh` runs the commands from its readme and writes their output into `expected/`. CI formats every entry point, runs every `expected.sh` and fails when `expected/` changes. A new sample also gets a `<Folder>` in `AslHelp.Memory.slnx`.

## Fixture

`test/AslHelp.Memory.Remote.Tests.Fixture` is a small console app the live patch tests start as a child. It exports `Tick`, prints `ready`, then calls `Tick` every 1 ms until its stdin closes. On Windows, the `net11.0` build of the Remote tests publishes it with NativeAOT to `artifacts/fixture/win-x64/` and `artifacts/fixture/win-x86/`. The 32-bit exe is linked without large address support, so its user space ends at 2 GB. The tests read both paths from the test assembly's metadata and end as inconclusive when an exe is missing. Each child is tied to the test host, so it ends when the host dies. The 1st publish needs the VC++ build tools (`link.exe`) with the x64 and x86 libraries and takes about a minute.
