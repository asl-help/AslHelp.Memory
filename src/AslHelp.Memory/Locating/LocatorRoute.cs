using AslHelp.Memory.Locating.Errors;
using System;
using System.Collections.Generic;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Represents a named list of steps from the base of a module to an address.
/// </summary>
/// <remarks>
///     The route starts at the module's base address. Each step turns the addresses the previous step reached into
///     the next ones. The route succeeds when every address the last step reached is the same.
/// </remarks>
public sealed class LocatorRoute
{
    private readonly RouteStep[] _steps;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LocatorRoute"/> class.
    /// </summary>
    /// <param name="name">The name of the route, such as the build it was written for.</param>
    /// <param name="steps">The steps to run in order.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="name"/>, <paramref name="steps"/> or 1 of the steps is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="steps"/> is empty. An <see cref="ExportStep"/> or a search of the whole module isn't the
    ///     first step.
    /// </exception>
    public LocatorRoute(string name, params RouteStep[] steps)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Length == 0)
        {
            throw new ArgumentException($"The route '{name}' needs at least 1 step.", nameof(steps));
        }

        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i] ?? throw new ArgumentNullException(nameof(steps), $"Step {i + 1} of the route '{name}' is null.");

            if (i > 0 && step is ExportStep or SearchStep { IsWholeModule: true })
            {
                throw new ArgumentException(
                    $"Step {i + 1} of the route '{name}' ({step}) starts from the module, so it has to be the first step.",
                    nameof(steps));
            }
        }

        Name = name;
        _steps = [.. steps];
    }

    /// <summary>
    ///     Gets the name of the route.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the steps of the route in order.
    /// </summary>
    public IReadOnlyList<RouteStep> Steps => _steps;

    /// <summary>
    ///     Runs the steps from the base of <paramref name="module"/>.
    /// </summary>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="module">The module the route starts at.</param>
    /// <returns>
    ///     The address the route reached on success, otherwise a failed result with the step that failed.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/> or <paramref name="module"/> is <see langword="null"/>.
    /// </exception>
    public Result<Address> Locate(IMemoryReader reader, MemoryModule module)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(module);

        List<Address> reached = [module.Range.Start];
        List<Address> next = [];

        for (int i = 0; i < _steps.Length; i++)
        {
            next.Clear();

            var run = _steps[i].Run(reader, module, reached, next);
            if (run.IsErr)
            {
                return LocatorError.StepFailed(i + 1, _steps[i], run.Error);
            }

            (reached, next) = (next, reached);
        }

        foreach (var address in reached)
        {
            if (address != reached[0])
            {
                return LocatorError.Ambiguous(reached[0], address);
            }
        }

        return reached[0];
    }

    /// <summary>
    ///     Formats the route as its name and its steps joined by <c>then</c>.
    /// </summary>
    /// <returns>
    ///     The formatted route.
    /// </returns>
    public override string ToString()
    {
        return $"{Name}: {string.Join(" then ", (IEnumerable<RouteStep>)_steps)}";
    }
}
