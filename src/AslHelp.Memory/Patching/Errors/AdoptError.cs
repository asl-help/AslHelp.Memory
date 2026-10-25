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
    ///     Creates the error for an entry that isn't redirected.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError NotRedirected()
    {
        return new("The entry isn't redirected.");
    }

    /// <summary>
    ///     Creates the error for an entry redirected by something other than this library.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError NotOurs()
    {
        return new("The entry is redirected by something other than this library, so it's left alone.");
    }

    /// <summary>
    ///     Creates the error for a patch written by another version of this library.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError AnotherVersion()
    {
        return new("The entry has a patch from another version of this library, which only that version can remove.");
    }

    /// <summary>
    ///     Creates the error for a page that claims a different entry than the one it was reached from.
    /// </summary>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError ClaimsAnotherEntry()
    {
        return new("The page claims a different entry, so nothing is assumed about it.");
    }

    /// <summary>
    ///     Creates the error for a page whose displaced length isn't one this library writes.
    /// </summary>
    /// <param name="stolen">The length the page records.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError ImplausibleStolenLength(int stolen)
    {
        return new($"The page records {stolen} displaced bytes, which isn't a length this library writes, so it's left alone.");
    }

    /// <summary>
    ///     Creates the error for a call read on a patch that only counts.
    /// </summary>
    /// <param name="entry">The patched entry.</param>
    /// <returns>
    ///     The error.
    /// </returns>
    public static AdoptError CapturesNothing(Address entry)
    {
        return new($"The patch at {entry} captures nothing, so there's no call to read.");
    }
}
