using System;

namespace AslHelp.Memory;

/// <summary>
///     Represents the file version in the version resource of a module file.
/// </summary>
/// <param name="Text">The file version text, such as <c>10.0.26100.1 (WinBuild.160101.0800)</c>, <see langword="null"/> when the file doesn't have one.</param>
/// <param name="Version">The 4 numbers of the fixed file version, 0.0.0.0 when the file doesn't have a version resource. They don't come from <paramref name="Text"/> and can differ from it.</param>
public sealed record ModuleFileVersion(
    string? Text,
    Version Version);
