namespace AslHelp.Memory;

/// <summary>
///     Represents a module loaded in a target's memory.
/// </summary>
/// <param name="Name">The file name of the module, such as <c>kernel32.dll</c>.</param>
/// <param name="Path">The full path of the module file, <see langword="null"/> when the path isn't known.</param>
/// <param name="Range">The bytes the module's image spans.</param>
public sealed record MemoryModule(
    string Name,
    string? Path,
    MemoryRange Range);
