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
        return new(SR.FormatLocatorStepFailed(number, step, reason.Message));
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
        return new(SR.FormatLocatorAmbiguous(first, other));
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
        return new(SR.FormatLocatorNoRouteLocated(failures));
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
        return new(step.Scan.Kind == Scanning.ScanStepKind.Backward
            ? SR.FormatLocatorNotFoundBefore(step.Scan.Pattern, step.Scan.WindowSize, anchor)
            : SR.FormatLocatorNotFoundFrom(step.Scan.Pattern, step.Scan.WindowSize, anchor));
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
        return new(SR.FormatLocatorNotFoundInModule(step.Scan.Pattern, module.Name));
    }
}
