using AslHelp.Memory.Errors;
using System;
using System.IO;
using System.Security.Cryptography;

namespace AslHelp.Memory;

/// <summary>
///     Represents a module loaded in a target's memory.
/// </summary>
/// <param name="Name">The file name of the module, such as <c>kernel32.dll</c>.</param>
/// <param name="Path">The full path of the module file, <see langword="null"/> when the path isn't known.</param>
/// <param name="Range">The bytes the module's image spans.</param>
public sealed record MemoryModule(
    string Name,
    string? Path,
    MemoryRange Range)
{
    private string? _fileHash;

    // Copies only the positional values, so a copy made with `with` hashes its own file.
    private MemoryModule(MemoryModule original)
    {
        Name = original.Name;
        Path = original.Path;
        Range = original.Range;
    }

    /// <summary>
    ///     Gets the SHA256 hash of the module file.
    /// </summary>
    /// <remarks>
    ///     Each instance reads the file once. Later calls return the same hash, even when the file changed on disk since.
    ///     An equal module from another <see cref="IMemoryModuleProvider.GetModules"/> call reads it again. A failure isn't
    ///     kept, so the next call tries again.
    /// </remarks>
    /// <returns>
    ///     The hash as 64 lowercase hex digits on success, otherwise a failed result when <see cref="Path"/> is
    ///     <see langword="null"/> or the file couldn't be read.
    /// </returns>
    public Result<string> GetFileHash()
    {
        if (_fileHash is not null)
        {
            return _fileHash;
        }

        if (Path is null)
        {
            return MemoryError.ModulePathUnknown(Name);
        }

        try
        {
            // Lets other handles write and delete the file, because a running game keeps its own files open.
            using FileStream stream = new(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha256 = SHA256.Create();

            _fileHash = Convert.ToHexStringLower(sha256.ComputeHash(stream));
            return _fileHash;
        }
        // Catches ArgumentException and NotSupportedException too, because an empty or malformed path throws them.
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return MemoryError.ModuleFileUnreadable(Path, ex.Message);
        }
    }

    /// <summary>
    ///     Determines whether <paramref name="other"/> has the same name, path and range.
    /// </summary>
    /// <remarks>
    ///     The kept file hash isn't compared.
    /// </remarks>
    /// <param name="other">The module to compare to.</param>
    /// <returns>
    ///     <see langword="true"/> when the modules are equal.
    /// </returns>
    public bool Equals(MemoryModule? other)
    {
        return other is not null
            && Name == other.Name
            && Path == other.Path
            && Range == other.Range;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return (Name, Path, Range).GetHashCode();
    }
}
