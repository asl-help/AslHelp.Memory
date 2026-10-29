using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Represents 1 step of a <see cref="LocatorRoute"/>, which turns each address the previous step reached into the
///     next one.
/// </summary>
/// <remarks>
///     Every step adds its <see cref="Offset"/> to the address its own work returns. The types that derive from it
///     are <see cref="ExportStep"/>, <see cref="SearchStep"/>, <see cref="ReadStep"/> and <see cref="OffsetStep"/>.
/// </remarks>
/// <param name="Offset">The number of bytes to add to each address the step returns, negative to move back.</param>
[JsonConverter(typeof(RouteStepConverter))]
public abstract record RouteStep(
    int Offset)
{
    /// <summary>
    ///     Gets the offset as it follows the step in <see cref="object.ToString"/>.
    /// </summary>
    private protected string OffsetSuffix => Offset switch
    {
        0 => "",
        < 0 => $" minus 0x{-(long)Offset:X}",
        _ => $" plus 0x{Offset:X}",
    };

    /// <summary>
    ///     Executes the step on each of <paramref name="from"/> and adds the addresses it reaches to <paramref name="to"/>.
    /// </summary>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="module">The module the route starts at.</param>
    /// <param name="from">The addresses the previous step reached.</param>
    /// <param name="to">The list to add the reached addresses to.</param>
    /// <returns>
    ///     A successful result when the step reached at least 1 address, otherwise the last failure.
    /// </returns>
    internal virtual Result Execute(IMemoryReader reader, MemoryModule module, List<Address> from, List<Address> to)
    {
        ResultError? failure = null;

        foreach (var address in from)
        {
            if (Resolve(reader, module, address).TryUnwrap(out var resolved, out var error))
            {
                to.Add(resolved + Offset);
            }
            else
            {
                failure = error;
            }
        }

        return to.Count > 0 ? Result.Ok() : failure!;
    }

    /// <summary>
    ///     Resolves 1 address into the next one, before the offset is added.
    /// </summary>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="module">The module the route starts at.</param>
    /// <param name="from">The address the previous step reached.</param>
    /// <returns>
    ///     The next address on success, otherwise a failed result.
    /// </returns>
    private protected abstract Result<Address> Resolve(IMemoryReader reader, MemoryModule module, Address from);
}
