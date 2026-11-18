namespace AslHelp.Memory.Errors;

/// <summary>
///     Represents a failed write to a target's memory, a module that isn't loaded or a module file that can't be hashed.
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

    /// <summary>
    ///     Creates the error for a module file at <paramref name="path"/> that couldn't be read.
    /// </summary>
    /// <param name="path">The full path of the module file.</param>
    /// <param name="reason">The system message for the failure.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError ModuleFileUnreadable(string path, string reason)
    {
        return new(SR.FormatModuleFileUnreadable(path, reason));
    }

    /// <summary>
    ///     Creates the error for a module called <paramref name="name"/> whose path isn't known.
    /// </summary>
    /// <param name="name">The file name of the module.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static MemoryError ModulePathUnknown(string name)
    {
        return new(SR.FormatModulePathUnknown(name));
    }
}
