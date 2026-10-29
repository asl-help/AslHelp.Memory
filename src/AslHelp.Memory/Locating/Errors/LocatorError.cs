namespace AslHelp.Memory.Locating.Errors;

/// <summary>
///     Represents a route or locator that didn't reach an address.
/// </summary>
internal sealed record LocatorError : ResultError
{
    private LocatorError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a step that reached nothing.
    /// </summary>
    /// <param name="number">The number of the step, counted from 1.</param>
    /// <param name="step">The step.</param>
    /// <param name="reason">The error of the step.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static LocatorError StepFailed(int number, RouteStep step, ResultError reason)
    {
        return new($"Step {number} ({step}) failed. {reason.Message}");
    }

    /// <summary>
    ///     Creates the error for a route whose steps reached more than 1 address.
    /// </summary>
    /// <param name="first">The first address.</param>
    /// <param name="other">An address that differs from it.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static LocatorError Ambiguous(Address first, Address other)
    {
        return new($"The route reaches both {first} and {other}. A search of the whole module has to lead to 1 address.");
    }

    /// <summary>
    ///     Creates the error for a locator whose routes all failed.
    /// </summary>
    /// <param name="failures">Each route's name with its error.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static LocatorError NoRouteLocated(string failures)
    {
        return new($"No route located the address. {failures}");
    }

    /// <summary>
    ///     Creates the error for a pattern that isn't in a window.
    /// </summary>
    /// <param name="step">The step that searched.</param>
    /// <param name="anchor">The address the window starts or ends at.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static LocatorError NotFoundNear(SearchStep step, Address anchor)
    {
        string side = step.Search.Kind == Scanning.ScanStepKind.Backward ? "before" : "from";
        return new($"'{step.Search.Pattern}' isn't in the {step.Search.WindowSize} bytes {side} {anchor}.");
    }

    /// <summary>
    ///     Creates the error for a pattern that isn't in a module.
    /// </summary>
    /// <param name="step">The step that searched.</param>
    /// <param name="module">The module that was searched.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static LocatorError NotFoundInModule(SearchStep step, MemoryModule module)
    {
        return new($"'{step.Search.Pattern}' isn't in '{module.Name}'.");
    }
}
