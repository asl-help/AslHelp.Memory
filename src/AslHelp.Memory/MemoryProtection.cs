using System;

namespace AslHelp.Memory;

/// <summary>
///     Specifies the access a region of a target's memory allows.
/// </summary>
[Flags]
public enum MemoryProtection
{
    /// <summary>
    ///     The region allows no access.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The region can be read.
    /// </summary>
    Read = 1,

    /// <summary>
    ///     The region can be written.
    /// </summary>
    Write = 2,

    /// <summary>
    ///     The region can be run as code.
    /// </summary>
    Execute = 4,
}
