using System.Globalization;

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
        return new(SR.FormatPeBadDosSignature(module));
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
        return new(SR.FormatPeBadNtSignature(module));
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
        return new(SR.FormatPeUnknownMagic(module, magic.ToString("X", CultureInfo.InvariantCulture)));
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
        return new(SR.FormatPeOutsideImage(size, rva.ToString("X", CultureInfo.InvariantCulture), module));
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
        return new(SR.FormatPeNoExportDirectory(module));
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
        return new(SR.FormatPeTooManyExportNames(module, count, max));
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
        return new(SR.FormatPeOrdinalOutOfRange(name, module, ordinal));
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
        return new(SR.FormatPeForwardedExport(name, module));
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
        return new(SR.FormatPeExportNotFound(module, name));
    }
}
