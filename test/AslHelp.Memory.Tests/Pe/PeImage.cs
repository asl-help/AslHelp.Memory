using AsmResolver;
using AsmResolver.PE;
using AsmResolver.PE.Builder;
using AsmResolver.PE.Exports;
using AsmResolver.PE.File;
using System;
using System.Collections.Generic;
using System.IO;

namespace AslHelp.Memory.Tests.Pe;

/// <summary>
///     Represents a synthetic native PE image with an export table, laid out as the loader maps it.
/// </summary>
/// <param name="File">The PE file the image was built from.</param>
/// <param name="Bytes">The mapped image, with each section at its RVA.</param>
/// <param name="Exports">The export directory, with the final RVA of each symbol.</param>
internal sealed record PeImage(
    PEFile File,
    byte[] Bytes,
    ExportDirectory Exports)
{
    /// <summary>
    ///     Builds an image that exports <paramref name="names"/> in the given order.
    /// </summary>
    /// <remarks>
    ///     An unnamed export comes first, so each named export's ordinal differs from its place in the name table.
    /// </remarks>
    /// <param name="is64Bit">Whether the image is PE32+.</param>
    /// <param name="names">The export names, in name table order.</param>
    /// <param name="forwarded">The name that forwards to <c>NTDLL.Target</c>, if any.</param>
    /// <param name="configure">Changes the PE file before it's written.</param>
    /// <returns>
    ///     The image.
    /// </returns>
    public static PeImage Build(
        bool is64Bit,
        IReadOnlyList<string> names,
        string? forwarded = null,
        Action<PEFile>? configure = null)
    {
        DataSegment code = new(new byte[(names.Count + 1) * 0x10]);
        PEImage image = new()
        {
            MachineType = is64Bit ? MachineType.Amd64 : MachineType.I386,
            PEKind = is64Bit ? OptionalHeaderMagic.PE32Plus : OptionalHeaderMagic.PE32,
        };

        ExportDirectory exports = new("test.dll");
        exports.Entries.Add(new ExportedSymbol(code.ToReference()));

        for (int i = 0; i < names.Count; i++)
        {
            var address = code.ToReference((i + 1) * 0x10);
            exports.Entries.Add(names[i] == forwarded
                ? new ExportedSymbol(address, names[i], "NTDLL.Target")
                : new ExportedSymbol(address, names[i]));
        }

        image.Exports = exports;

        var file = new TemplatedPEFileBuilder().CreateFile(image);
        file.Sections.Add(new PESection(".text", SectionFlags.ContentCode | SectionFlags.MemoryRead, code));
        file.UpdateHeaders();
        configure?.Invoke(file);

        return new(file, Map(file), exports);
    }

    /// <summary>
    ///     Gets the RVA of the function exported as <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The export name.</param>
    /// <returns>
    ///     The RVA.
    /// </returns>
    public uint FunctionRva(string name)
    {
        foreach (var symbol in Exports.Entries)
        {
            if (symbol.Name == name)
            {
                return symbol.Address.Rva;
            }
        }

        throw new ArgumentException($"'{name}' isn't exported.", nameof(name));
    }

    private static byte[] Map(PEFile file)
    {
        using MemoryStream stream = new();
        file.Write(stream);

        byte[] mapped = new byte[file.OptionalHeader.SizeOfImage];
        Array.Copy(stream.ToArray(), mapped, file.OptionalHeader.SizeOfHeaders);

        foreach (var section in file.Sections)
        {
            byte[] contents = section.WriteIntoArray();
            Array.Copy(contents, 0, mapped, section.Rva, Math.Min(contents.Length, mapped.Length - (int)section.Rva));
        }

        return mapped;
    }
}
