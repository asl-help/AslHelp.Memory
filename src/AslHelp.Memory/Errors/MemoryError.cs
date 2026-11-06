namespace AslHelp.Memory.Errors;

/// <summary>
///     Represents a failed write to a target's memory or a module that isn't loaded.
/// </summary>
internal sealed record MemoryError : ResultError
{
    private MemoryError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for <paramref name="size"/> bytes at <paramref name="address"/> that couldn't be written.
    /// </summary>
    /// <param name="address">The address of the first byte.</param>
    /// <param name="size">The number of bytes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError Unwritable(Address address, int size)
    {
        return new(SR.FormatUnwritable(size, address));
    }

    /// <summary>
    ///     Creates the error for a module called <paramref name="name"/> that isn't loaded.
    /// </summary>
    /// <param name="name">The file name of the module.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError ModuleNotFound(string name)
    {
        return new(SR.FormatModuleNotFound(name));
    }
}
