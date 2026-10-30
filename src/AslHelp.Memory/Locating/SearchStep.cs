using AslHelp.Memory.Locating.Errors;
using AslHelp.Memory.Scanning;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Represents the step that searches for a pattern, either in a window around the previous address or in the
///     whole module.
/// </summary>
/// <remarks>
///     A <see cref="ScanWindow.Forward(int)"/> window takes its first match and a
///     <see cref="ScanWindow.Backward(int)"/> window takes its last, so both take the match nearest to the previous
///     address. A <see cref="Scan.Create(string)"/> search goes through the whole module and keeps every
///     match. The route then fails unless they all lead to the same address.
/// </remarks>
/// <param name="Scan">The scan step to run, made with <see cref="Scanning.Scan"/>, without a resolver.</param>
/// <param name="Offset">The number of bytes from a match to the address the step returns.</param>
public sealed record SearchStep(
    ScanStep Scan,
    int Offset = 0) : RouteStep(Offset)
{
    // A window may reach past the module, so it's only clamped to the whole address space.
    private static readonly MemoryRange _addressSpace = new(Address.Zero, ulong.MaxValue);

    /// <summary>
    ///     Gets the scan step to run.
    /// </summary>
    public ScanStep Scan { get; } = Validate(Scan);

    /// <summary>
    ///     Gets a value indicating whether the step searches the whole module.
    /// </summary>
    public bool IsWholeModule => Scan.Kind == ScanStepKind.Range;

    /// <summary>
    ///     Formats the step as in <c>forward 0x100 for '48 8B 0D' plus 0x3</c>.
    /// </summary>
    /// <returns>
    ///     The formatted step.
    /// </returns>
    public override string ToString()
    {
        return Scan.Kind switch
        {
            ScanStepKind.Forward => $"forward 0x{Scan.WindowSize:X} for '{Scan.Pattern}'{OffsetSuffix}",
            ScanStepKind.Backward => $"backward 0x{Scan.WindowSize:X} for '{Scan.Pattern}'{OffsetSuffix}",
            _ => $"search the module for '{Scan.Pattern}'{OffsetSuffix}",
        };
    }

    internal override Result Execute(IMemoryReader reader, MemoryModule module, List<Address> from, List<Address> to)
    {
        if (!IsWholeModule)
        {
            return base.Execute(reader, module, from, to);
        }

        foreach (var match in Scanning.Scan.ScanFrom(reader, module.Range, Scan, module.Range.Start))
        {
            to.Add(match + Offset);
        }

        return to.Count > 0 ? Result.Ok() : LocatorError.NotFoundInModule(this, module);
    }

    private protected override Result<Address> Resolve(IMemoryReader reader, MemoryModule module, Address from)
    {
        bool found = false;
        var nearest = Address.Zero;

        foreach (var match in Scanning.Scan.ScanFrom(reader, _addressSpace, Scan, from))
        {
            found = true;
            nearest = match;

            if (Scan.Kind == ScanStepKind.Forward)
            {
                break;
            }
        }

        return found ? nearest : LocatorError.NotFoundNear(this, from);
    }

    private static ScanStep Validate(ScanStep scan)
    {
        if (scan.Pattern.Length == 0)
        {
            throw new ArgumentException(
                "The scan step has no pattern. Make it with Scan.Create or with Create on a ScanWindow.Forward or ScanWindow.Backward window.",
                nameof(scan));
        }

        if (scan.Resolver is not null)
        {
            throw new ArgumentException(
                "The scan step of a search step can't have a resolver. Add a read step after it instead.",
                nameof(scan));
        }

        return scan;
    }
}
