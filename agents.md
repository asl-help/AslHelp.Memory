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
