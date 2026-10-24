using AslHelp.Memory.Pe;
using System;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Represents the step that finds a named export of the module the route starts at.
/// </summary>
/// <remarks>
///     The step ignores the address before it, so it's only allowed as the first step of a route.
/// </remarks>
/// <param name="Name">The name of the export, such as <c>mono_get_root_domain</c>.</param>
/// <param name="Offset">The number of bytes to add to the export's address.</param>
public sealed record ExportStep(
    string Name,
    int Offset = 0) : RouteStep(Offset)
{
    /// <summary>
    ///     Gets the name of the export.
    /// </summary>
    public string Name { get; } = Name ?? throw new ArgumentNullException(nameof(Name));

    /// <summary>
    ///     Formats the step as in <c>export 'mono_get_root_domain' plus 0x3</c>.
    /// </summary>
    /// <returns>
    ///     The formatted step.
    /// </returns>
    public override string ToString()
    {
        return $"export '{Name}'{OffsetSuffix}";
    }

    private protected override Result<Address> Next(IMemoryReader reader, MemoryModule module, Address from)
    {
        return reader.GetExport(module, Name);
    }
}
