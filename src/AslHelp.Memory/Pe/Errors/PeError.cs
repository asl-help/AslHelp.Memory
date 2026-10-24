namespace AslHelp.Memory.Pe.Errors;

/// <summary>
///     Represents a module whose PE headers or export table couldn't be read.
/// </summary>
internal sealed record PeError : ResultError
{
    private PeError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a module that doesn't start with the DOS signature.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError BadDosSignature(string module)
    {
        return new($"'{module}' doesn't start with the DOS signature 'MZ'.");
    }

    /// <summary>
    ///     Creates the error for a module whose NT headers don't start with the NT signature.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError BadNtSignature(string module)
    {
        return new($"The NT headers of '{module}' don't start with the NT signature 'PE'.");
    }

    /// <summary>
    ///     Creates the error for an optional header magic that isn't PE32 or PE32+.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <param name="magic">The magic the optional header starts with.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError UnknownMagic(string module, ushort magic)
    {
        return new($"The optional header of '{module}' has the magic 0x{magic:X}. Only PE32 (0x10B) and PE32+ (0x20B) are read.");
    }

    /// <summary>
    ///     Creates the error for <paramref name="size"/> bytes at <paramref name="rva"/> that reach past the image.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <param name="rva">The offset of the first byte from the module's base address.</param>
    /// <param name="size">The number of bytes.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError OutsideImage(string module, ulong rva, int size)
    {
        return new($"{size} bytes at offset 0x{rva:X} reach past the image of '{module}'.");
    }

    /// <summary>
    ///     Creates the error for a module without an export table.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError NoExportDirectory(string module)
    {
        return new($"'{module}' has no export table.");
    }

    /// <summary>
    ///     Creates the error for a name table longer than any real image has.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <param name="count">The number of names the table claims.</param>
    /// <param name="max">The largest number of names that's read.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError TooManyExportNames(string module, uint count, uint max)
    {
        return new($"The export table of '{module}' claims {count} names. At most {max} are read.");
    }

    /// <summary>
    ///     Creates the error for a name whose ordinal points past the function table.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <param name="name">The export name.</param>
    /// <param name="ordinal">The ordinal the name points at.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError OrdinalOutOfRange(string module, string name, ushort ordinal)
    {
        return new($"'{name}' in '{module}' points at ordinal {ordinal}, past the end of the function table.");
    }

    /// <summary>
    ///     Creates the error for an export that forwards to a function in another module.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <param name="name">The export name.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError ForwardedExport(string module, string name)
    {
        return new($"'{name}' in '{module}' is a forwarded export. Its code is in another module.");
    }

    /// <summary>
    ///     Creates the error for a name that the module doesn't export.
    /// </summary>
    /// <param name="module">The file name of the module.</param>
    /// <param name="name">The export name.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static PeError ExportNotFound(string module, string name)
    {
        return new($"'{module}' doesn't export '{name}'.");
    }
}
