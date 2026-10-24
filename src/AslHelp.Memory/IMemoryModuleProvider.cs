using System.Collections.Generic;

namespace AslHelp.Memory;

/// <summary>
///     Provides the modules loaded in a target's memory.
/// </summary>
public interface IMemoryModuleProvider
{
    /// <summary>
    ///     Gets the modules currently loaded in the target.
    /// </summary>
    /// <returns>
    ///     The modules on success, otherwise a failed result.
    /// </returns>
    Result<IReadOnlyList<MemoryModule>> GetModules();
}
