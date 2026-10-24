using AslHelp.Memory.Pe.Errors;
using AslHelp.Memory.Pe.Native;
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static AslHelp.Memory.Pe.PeFormat;

namespace AslHelp.Memory.Pe;

/// <summary>
///     Provides reads of the PE headers and exports of a loaded module over an <see cref="IMemoryReader"/>.
/// </summary>
/// <remarks>
///     Every read stays inside the module's range. Each header is read as its PE structure in the host's byte order,
///     which matches the little-endian PE format on x86, x64 and ARM hosts.
/// </remarks>
public static class PeReaderExtensions
{
    // No real image has more named exports, so a longer table is treated as a torn read.
    private const int MaxExportNames = 0x10000;

    // Fits the name table of a module with up to 256 named exports.
    private const int NameTableStackBytes = 256 * sizeof(uint);

    // Fits an export name of up to 255 characters with its terminator.
    private const int NameStackBytes = 256;

    private const int MaxAscii = 0x7F;

    extension(IMemoryReader reader)
    {
        /// <summary>
        ///     Reads the entry for <paramref name="directory"/> from the data directory of <paramref name="module"/>.
        /// </summary>
        /// <remarks>
        ///     An image with fewer entries than <paramref name="directory"/> needs returns an empty entry.
        /// </remarks>
        /// <param name="module">The loaded module to read.</param>
        /// <param name="directory">The entry to read.</param>
        /// <returns>
        ///     The entry on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader or <paramref name="module"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="directory"/> is outside the 16 entries of a data directory.
        /// </exception>
        public Result<PeDataDirectory> GetDataDirectory(MemoryModule module, PeDirectory directory)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(module);

            if ((uint)directory >= IMAGE_NUMBEROF_DIRECTORY_ENTRIES)
            {
                throw new ArgumentOutOfRangeException(nameof(directory), directory, "A data directory has 16 entries.");
            }

            return ReadDataDirectory(reader, module, (int)directory);
        }

        /// <summary>
        ///     Finds the address of the export called <paramref name="name"/> in <paramref name="module"/>.
        /// </summary>
        /// <remarks>
        ///     The linker sorts the name table, so a binary search finds the name. When it misses, each name is
        ///     compared in turn, for an image whose table isn't sorted. Names compare as ASCII bytes with case. A
        ///     found export doesn't allocate anything. A name table of more than 256 names is read into a pooled
        ///     array. A table that claims more than 65536 names fails.
        /// </remarks>
        /// <param name="module">The loaded module to search.</param>
        /// <param name="name">The export name, such as <c>GetProcAddress</c>.</param>
        /// <returns>
        ///     The address of the export on success, otherwise a failed result. A forwarded export fails, because its
        ///     code is in another module.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The reader, <paramref name="module"/> or <paramref name="name"/> is <see langword="null"/>.
        /// </exception>
        public Result<Address> GetExport(MemoryModule module, string name)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(module);
            ArgumentNullException.ThrowIfNull(name);

            var exports = ReadDataDirectory(reader, module, IMAGE_DIRECTORY_ENTRY_EXPORT);
            if (!exports.TryUnwrap(out var directory, out var error))
            {
                return error;
            }

            if (directory.VirtualAddress == 0 || directory.Size == 0)
            {
                return PeError.NoExportDirectory(module.Name);
            }

            var exportDirectory = ReadImage<ImageExportDirectory>(reader, module, directory.VirtualAddress);
            if (!exportDirectory.TryUnwrap(out var table, out error))
            {
                return error;
            }

            uint count = table.NumberOfNames;
            if (count > MaxExportNames)
            {
                return PeError.TooManyExportNames(module.Name, count, MaxExportNames);
            }

            // The probe has room for 1 byte past the name, so a longer export name never compares as equal.
            using var target = new Scratch<byte>(stackalloc byte[NameStackBytes], name.Length + 1);
            using var probe = new Scratch<byte>(stackalloc byte[NameStackBytes], name.Length + 1);
            if (!TryEncodeAscii(name, target.Buffer))
            {
                return PeError.ExportNotFound(module.Name, name);
            }

            using var names = new Scratch<byte>(stackalloc byte[NameTableStackBytes], (int)count * sizeof(uint));
            var read = ReadImage(reader, module, table.AddressOfNames, names.Buffer);
            if (read.IsErr)
            {
                return read.Error;
            }

            int low = 0;
            int high = (int)count - 1;

            while (low <= high)
            {
                int middle = low + ((high - low) / 2);

                read = CompareName(reader, module, NameRva(names.Buffer, middle), target.Buffer, probe.Buffer, out int order);
                if (read.IsErr)
                {
                    return read.Error;
                }

                if (order == 0)
                {
                    return ExportAt(reader, module, directory, table, middle, name);
                }

                if (order < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            for (int i = 0; i < count; i++)
            {
                read = CompareName(reader, module, NameRva(names.Buffer, i), target.Buffer, probe.Buffer, out int order);
                if (read.IsErr)
                {
                    return read.Error;
                }

                if (order == 0)
                {
                    return ExportAt(reader, module, directory, table, i, name);
                }
            }

            return PeError.ExportNotFound(module.Name, name);
        }
    }

    private static Result<PeDataDirectory> ReadDataDirectory(IMemoryReader reader, MemoryModule module, int index)
    {
        var dos = ReadImage<ImageDosHeader>(reader, module, 0);
        if (!dos.TryUnwrap(out var dosHeader, out var error))
        {
            return error;
        }

        if (dosHeader.Magic != IMAGE_DOS_SIGNATURE)
        {
            return PeError.BadDosSignature(module.Name);
        }

        ulong ntOffset = (uint)dosHeader.Lfanew;

        var nt = ReadImage<ImageNtHeaders>(reader, module, ntOffset);
        if (!nt.TryUnwrap(out var ntHeaders, out error))
        {
            return error;
        }

        if (ntHeaders.Signature != IMAGE_NT_SIGNATURE)
        {
            return PeError.BadNtSignature(module.Name);
        }

        // The optional header starts at its Magic, the last field of ImageNtHeaders.
        ulong optionalOffset = ntOffset + (ulong)Unsafe.SizeOf<ImageNtHeaders>() - sizeof(ushort);
        uint entryCount;
        ulong entriesOffset;

        switch (ntHeaders.Magic)
        {
            case IMAGE_NT_OPTIONAL_HDR32_MAGIC:
            {
                var optional = ReadImage<ImageOptionalHeader32>(reader, module, optionalOffset);
                if (!optional.TryUnwrap(out var header, out error))
                {
                    return error;
                }

                entryCount = header.NumberOfRvaAndSizes;
                entriesOffset = optionalOffset + (ulong)Unsafe.SizeOf<ImageOptionalHeader32>();
                break;
            }
            case IMAGE_NT_OPTIONAL_HDR64_MAGIC:
            {
                var optional = ReadImage<ImageOptionalHeader64>(reader, module, optionalOffset);
                if (!optional.TryUnwrap(out var header, out error))
                {
                    return error;
                }

                entryCount = header.NumberOfRvaAndSizes;
                entriesOffset = optionalOffset + (ulong)Unsafe.SizeOf<ImageOptionalHeader64>();
                break;
            }
            default:
            {
                return PeError.UnknownMagic(module.Name, ntHeaders.Magic);
            }
        }

        if (index >= entryCount)
        {
            return default(PeDataDirectory);
        }

        ulong entryOffset = entriesOffset + ((ulong)index * (ulong)Unsafe.SizeOf<ImageDataDirectory>());

        var entry = ReadImage<ImageDataDirectory>(reader, module, entryOffset);
        if (!entry.TryUnwrap(out var data, out error))
        {
            return error;
        }

        return new PeDataDirectory(data.VirtualAddress, data.Size);
    }

    private static Result<Address> ExportAt(
        IMemoryReader reader,
        MemoryModule module,
        PeDataDirectory directory,
        in ImageExportDirectory table,
        int index,
        string name)
    {
        var ordinalRead = ReadImage<ushort>(reader, module, table.AddressOfNameOrdinals + ((ulong)index * sizeof(ushort)));
        if (!ordinalRead.TryUnwrap(out ushort ordinal, out var error))
        {
            return error;
        }

        if (ordinal >= table.NumberOfFunctions)
        {
            return PeError.OrdinalOutOfRange(module.Name, name, ordinal);
        }

        var functionRead = ReadImage<uint>(reader, module, table.AddressOfFunctions + ((ulong)ordinal * sizeof(uint)));
        if (!functionRead.TryUnwrap(out uint function, out error))
        {
            return error;
        }

        // A function inside the export directory is a forwarder string such as "NTDLL.RtlAllocateHeap", not code.
        if (directory.Contains(function))
        {
            return PeError.ForwardedExport(module.Name, name);
        }

        return module.Range.Start + function;
    }

    // Compares the export name at `rva` to `target` the way an ordinal string comparison does, reading only as many
    // bytes as `target` spans.
    private static Result CompareName(
        IMemoryReader reader,
        MemoryModule module,
        uint rva,
        ReadOnlySpan<byte> target,
        Span<byte> probe,
        out int order)
    {
        order = 0;

        // A name close to the end of the image is read only up to the end.
        ulong left = rva < module.Range.Size ? module.Range.Size - rva : 0;
        int length = (int)Math.Min((ulong)probe.Length, left);

        var read = ReadImage(reader, module, rva, probe[..Math.Max(length, 1)]);
        if (read.IsErr)
        {
            return read;
        }

        for (int i = 0; i < length; i++)
        {
            order = probe[i] - target[i];
            if (order != 0 || probe[i] == 0)
            {
                return Result.Ok();
            }
        }

        // The name runs to the end of the image without a terminator, so it's longer than the target.
        order = 1;
        return Result.Ok();
    }

    private static Result ReadImage(IMemoryReader reader, MemoryModule module, ulong rva, Span<byte> buffer)
    {
        ulong size = module.Range.Size;
        if (rva > size || (ulong)buffer.Length > size - rva)
        {
            return PeError.OutsideImage(module.Name, rva, buffer.Length);
        }

        return reader.Read(module.Range.Start + (long)rva, buffer);
    }

    private static Result<T> ReadImage<T>(IMemoryReader reader, MemoryModule module, ulong rva)
        where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()];

        var read = ReadImage(reader, module, rva, bytes);
        if (read.IsErr)
        {
            return read.Error;
        }

        return MemoryMarshal.Read<T>(bytes);
    }

    private static bool TryEncodeAscii(string name, Span<byte> target)
    {
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c > MaxAscii)
            {
                return false;
            }

            target[i] = (byte)c;
        }

        target[name.Length] = 0;
        return true;
    }

    private static uint NameRva(ReadOnlySpan<byte> names, int index)
    {
        return ReadUInt32(names, index * sizeof(uint));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }
}
