namespace AslHelp.Memory.Patching.Errors;

/// <summary>
///     Represents an entry whose patch couldn't be adopted or read.
/// </summary>
internal sealed record AdoptError : ResultError
{
    private AdoptError(string message)
    {
        Message = message;
    }

    /// <inheritdoc/>
    public override string Message { get; }

    /// <summary>
    ///     Creates the error for a call read on an adopted patch.
    /// </summary>
    /// <param name="entry">The patched entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError UnknownLayout(Address entry)
    {
        return new(SR.FormatAdoptUnknownLayout(entry));
    }

    /// <summary>
    ///     Creates the error for an entry whose bytes couldn't be read.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError UnreadableEntry(Address entry)
    {
        return new(SR.FormatAdoptUnreadableEntry(entry));
    }

    /// <summary>
    ///     Creates the error for a page whose patch length isn't the 5-byte jump.
    /// </summary>
    /// <param name="length">The patch length the page records.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError UnexpectedPatchLength(int length)
    {
        return new(SR.FormatAdoptUnexpectedPatchLength(length));
    }

    /// <summary>
    ///     Creates the error for an entry that isn't redirected.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError NotRedirected()
    {
        return new(SR.AdoptNotRedirected);
    }

    /// <summary>
    ///     Creates the error for an entry redirected by something other than this library.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError ForeignPatch()
    {
        return new(SR.AdoptForeignPatch);
    }

    /// <summary>
    ///     Creates the error for a patch written by another version of this library.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError AnotherVersion()
    {
        return new(SR.AdoptAnotherVersion);
    }

    /// <summary>
    ///     Creates the error for a page that claims a different entry than the one it was reached from.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError EntryMismatch()
    {
        return new(SR.AdoptEntryMismatch);
    }

    /// <summary>
    ///     Creates the error for a page whose displaced length isn't one this library writes.
    /// </summary>
    /// <param name="stolen">The length the page records.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError StolenLengthOutOfRange(int stolen)
    {
        return new(SR.FormatAdoptStolenLengthOutOfRange(stolen));
    }

    /// <summary>
    ///     Creates the error for a call read on a patch that only counts.
    /// </summary>
    /// <param name="entry">The patched entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError NoCaptures(Address entry)
    {
        return new(SR.FormatAdoptNoCaptures(entry));
    }
}
