namespace AslHelp.Memory;

/// <summary>
///     Represents a committed region of a target's memory with one protection across its bytes.
/// </summary>
/// <param name="Range">The bytes of the region.</param>
/// <param name="Protection">The access the region allows.</param>
public readonly record struct MemoryRegion(
    MemoryRange Range,
    MemoryProtection Protection);
