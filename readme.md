# AslHelp.Memory

[![AslHelp.Memory on NuGet](https://img.shields.io/nuget/vpre/AslHelp.Memory?label=AslHelp.Memory)](https://www.nuget.org/packages/AslHelp.Memory)
[![AslHelp.Memory.Remote on NuGet](https://img.shields.io/nuget/vpre/AslHelp.Memory.Remote?label=AslHelp.Memory.Remote)](https://www.nuget.org/packages/AslHelp.Memory.Remote)

AslHelp.Memory is a .NET library for reading another process's memory in any pointer size and byte order. The `AslHelp.Memory.Remote` package reads a running Windows process.

## Install

```
dotnet add package AslHelp.Memory
dotnet add package AslHelp.Memory.Remote
```

## Usage

### Reading

`RemoteProcessMemory` opens a process. Every read returns a `Result<T>` from `AslHelp.Shared`, so a failed read doesn't throw:

```cs
using var memory = new RemoteProcessMemory(process);

if (!memory.GetMainModule().TryUnwrap(out var module, out var error))
{
    return error;
}

var level = memory.Read<int>(module.Range.Start + 0x1234);
var name = memory.ReadString(module.Range.Start + 0x2000, StringType.Utf8);
var player = memory.Deref(module.Range.Start + 0x3000, 0x10, 0x8);
```

`ReadArray` reads several values at once and `ReadPointer` reads 1 pointer in the target's pointer size.

`(nint)address` and `(Address)pointer` convert between an `Address` and a native pointer. `(nint)address` throws `OverflowException` when the address doesn't fit the host's pointer size.

`GetModule` finds a module by name. `GetFileHash` returns the SHA256 of a module's file. Each `MemoryModule` reads its file once. `GetModule` returns a new one per call, so keep the module to reuse the hash.

### Scanning

`Scan` searches memory for a signature, where `??` matches any byte. `WithResolver` turns each match into the address it points to:

```cs
var step = Scan.Create("E8 ?? ?? ?? ??").WithResolver(AddressResolver.FromDisplacement(1));

foreach (var address in Scan.ScanRange(memory, module.Range, step))
{
    Console.WriteLine(address);
}
```

`Scan.ScanRegions` searches every readable region of the process. With a range it only searches the regions inside it. `ScanWindow.Forward(n)` and `ScanWindow.Backward(n)` make a window of n bytes around the previous step's match. `Create` on that window makes the step.

### Exports

`GetExport` finds a function that a module exports by name:

```cs
var getDomain = memory.GetExport(module, "mono_get_root_domain");
```

### Locating

A `MemoryLocator` finds an address in a module through named routes. Each route is a different way to the same address. `Locate` tries them until 1 of them works. A locator reads from JSON:

```jsonc
{
  "SceneManager_Get_Custom_PropSceneCount": [
    { "forward": [
        "48 8B 05 ????????",  // mov rax, [sceneManager]
        "8B 40 18",           // mov eax, [rax+0x18]
        "C3"                  // ret
      ], "offset": 3 },
    { "read": "global" }
  ]
}
```

```cs
var locator = JsonSerializer.Deserialize<MemoryLocator>(json, options);
var sceneManager = locator.Locate(memory, module);
```

`Routes["name"]` runs only 1 route. `GlobalPointer` locates a global once and reads the pointer in it on each access.

### Watching a value

A watcher reads a value at the end of a pointer path and keeps the read before it. It reads at most once per tick of an `ITickCounter` and only when one of its values is used:

```cs
var ticks = new Ticks();
var level = new MemoryWatcher<int>(ticks, memory, module.Range.Start + 0x1234, 0x10, 0x8);

while (true)
{
    ticks.Value++;

    if (level.Changed)
    {
        Console.WriteLine($"Level went from {level.Old} to {level.Current}.");
    }

    Thread.Sleep(15);
}

sealed class Ticks : ITickCounter
{
    public long Value { get; set; }
}
```

The first read only fills the values, so `Changed` can only be `true` from the second read on.

`ChangedTo` and `ChangedFrom` check what the value changed to or from. `StringWatcher` and `ArrayWatcher<T>` work the same way for a string and for a fixed number of values.

When a read fails, `FailAction` picks what happens. `Clear` resets the values, so the next good read only fills them again. `KeepOld` keeps them and `Throw` throws with the error from the read. `Error` has the error of the latest read under every action and is `null` after a good read. It doesn't read itself.

`Watcher` gives `Current` and `Old` as `object` too, for code that only has a `Watcher`. An unchanged value keeps its box, so it doesn't allocate on each tick.

Derive from `Watcher<T>` and override `Read` to watch a value that doesn't come from memory:

```cs
sealed class DoubledWatcher(ITickCounter ticks, Watcher<int> source) : Watcher<int>(ticks)
{
    protected override Result<int> Read()
    {
        int value = source.Current;
        return source.Error is { } error ? error : value * 2;
    }
}
```

`Ticks` is the tick counter the watcher got. It reads at most once per tick like the others and `FailAction` and `Enabled` work the same. A `Read` that throws sets `Error` to an `ExceptionError` and its exception comes back out under every `FailAction`, which only decides what happens to the values. A disabled watcher keeps its last `Error`.

### Patching

A `PatchSession` redirects a function's entry into a stub that counts each call. `RemotePatchWriter` writes the patches into a running Windows process:

```cs
using var writer = new RemotePatchWriter(memory);
using var session = new PatchSession(memory, writer);

var patch = session.Install(entry).Unwrap();
var calls = patch.ReadCount(memory);
```

Disposing the session puts every entry back.

## Contributing

You need the .NET SDK version from `global.json`. These are the commands CI runs:

```
dotnet build
dotnet test
dotnet format --severity info --verify-no-changes
```

The `net4.8.1` tests only run on Windows.

## License

[0BSD](license.md)
