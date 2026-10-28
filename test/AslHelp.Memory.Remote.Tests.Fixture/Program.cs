using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AslHelp.Memory.Remote.Tests.Fixture;

internal static unsafe class Program
{
    private static ulong _ticks;

    private static void Main()
    {
        // Exits once stdin closes, so a child the test host left behind ends with the host.
        Thread watcher = new(() =>
        {
            _ = Console.In.ReadLine();
            Environment.Exit(0);
        })
        {
            IsBackground = true,
        };

        watcher.Start();

        Console.WriteLine("ready");

        // Calls Tick through a native pointer, so every call enters its compiled entry and a patch there sees it.
        delegate* unmanaged<void> tick = &Tick;

        while (true)
        {
            tick();

            // Sleeps 1 ms per call, so the main thread almost never stops inside the 5 bytes a patch displaces.
            Thread.Sleep(1);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Tick")]
    private static void Tick()
    {
        // Counts through Interlocked, so the compiler keeps the store and the method keeps a body to patch.
        _ = Interlocked.Increment(ref _ticks);
    }
}
