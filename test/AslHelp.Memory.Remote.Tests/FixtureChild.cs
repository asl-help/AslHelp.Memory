using AslHelp.Memory.Patching;
using AslHelp.Memory.Pe;

using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;

namespace AslHelp.Memory.Remote.Tests;

/// <summary>
///     Represents the fixture exe running as a child, opened for reading and patching.
/// </summary>
/// <remarks>
///     The child calls its exported <c>Tick</c> every 1 ms until its stdin closes, so a patch on <c>Tick</c> counts
///     calls from real compiled code. The child ends with the test host, so a host that dies while the child is
///     suspended leaves no process behind to lock the published exe.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class FixtureChild : IDisposable
{
    private const string ExeKey = "FixtureExe";
    private const string Exe32Key = "FixtureExe32";

    private FixtureChild(
        Process process,
        RemoteProcessMemory memory,
        Address tick)
    {
        Process = process;
        Memory = memory;
        Tick = tick;
    }

    /// <summary>
    ///     Gets the running child.
    /// </summary>
    public Process Process { get; }

    /// <summary>
    ///     Gets the child's memory.
    /// </summary>
    public RemoteProcessMemory Memory { get; }

    /// <summary>
    ///     Gets the address the child's <c>Tick</c> export begins at.
    /// </summary>
    public Address Tick { get; }

    /// <summary>
    ///     Starts the 64-bit fixture and waits for its ready line.
    /// </summary>
    /// <remarks>
    ///     Ends the calling test as inconclusive when the exe is missing or the host is 32-bit, because a 32-bit host
    ///     can't read the thread context of the 64-bit child.
    /// </remarks>
    /// <returns>
    ///     The running child.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The child exited before it printed its ready line.
    /// </exception>
    public static FixtureChild Start()
    {
        Assume.That(Environment.Is64BitProcess, Is.True, "The fixture is 64-bit and the host is not.");

        return Start(ExePath(ExeKey));
    }

    /// <summary>
    ///     Starts the 32-bit fixture and waits for its ready line.
    /// </summary>
    /// <remarks>
    ///     Ends the calling test as inconclusive when the exe is missing. The exe is linked without large address
    ///     support, so its user space ends at 2 GB.
    /// </remarks>
    /// <returns>
    ///     The running child.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     The child exited before it printed its ready line.
    /// </exception>
    public static FixtureChild Start32Bit()
    {
        return Start(ExePath(Exe32Key));
    }

    private static FixtureChild Start(string path)
    {
        Assume.That(File.Exists(path), Is.True, $"The fixture is not published at {path}.");

        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
#if NET11_0_OR_GREATER
            KillOnParentExit = true,
#endif
        };

        var process = Process.Start(info) ?? throw new InvalidOperationException($"The fixture at {path} did not start.");

        RemoteProcessMemory? memory = null;

        try
        {
#if !NET11_0_OR_GREATER
            HostJob.Add(process);
#endif

            // Waits for the handshake, so the runtime is up and the main thread already loops through Tick.
            string? line;

            do
            {
                line = process.StandardOutput.ReadLine();
            }
            while (line is not null and not "ready");

            if (line is null)
            {
                process.WaitForExit();

                throw new InvalidOperationException($"The fixture at {path} exited with code {process.ExitCode} before it was ready.");
            }

            memory = new RemoteProcessMemory(process);

            var module = memory.GetModule(Path.GetFileName(path)).Unwrap();
            var tick = memory.GetExport(module, "Tick").Unwrap();

            var child = new FixtureChild(process, memory, tick);
            memory = null;
            process = null;

            return child;
        }
        finally
        {
            memory?.Dispose();

            if (process is not null)
            {
                Stop(process);
            }
        }
    }

    /// <summary>
    ///     Opens a new patch writer on the child.
    /// </summary>
    /// <returns>
    ///     The writer.
    /// </returns>
    public RemotePatchWriter NewWriter()
    {
        return new(Memory);
    }

    /// <summary>
    ///     Waits until <paramref name="patch"/> has counted more than <paramref name="above"/> calls.
    /// </summary>
    /// <param name="patch">The patch to read the count of.</param>
    /// <param name="above">The count to wait past.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>
    ///     Whether the count rose past <paramref name="above"/> in time.
    /// </returns>
    public bool WaitForCount(CodePatch patch, ulong above, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < timeout)
        {
            if (patch.ReadCount(Memory).TryUnwrap(out ulong count) && count > above)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    /// <summary>
    ///     Closes the child's stdin so it exits. Kills it when it is still alive after 2 s.
    /// </summary>
    public void Dispose()
    {
        Memory.Dispose();
        Stop(Process);
    }

    private static string ExePath(string key)
    {
        foreach (var metadata in typeof(FixtureChild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Key == key && metadata.Value is { Length: > 0 } path)
            {
                return path;
            }
        }

        throw new InvalidOperationException($"The test assembly has no {key} metadata.");
    }

    private static void Stop(Process process)
    {
        try
        {
            process.StandardInput.Close();

            if (!process.WaitForExit(2000))
            {
                process.Kill();
                process.WaitForExit();
            }
        }
        finally
        {
            process.Dispose();
        }
    }
}
