using AslHelp.Memory.Scanning;
using AslHelp.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Provides the JSON form of a <see cref="RouteStep"/>: an object whose 1 key is the kind of step, with the
///     settings the step takes next to it.
/// </summary>
/// <remarks>
///     For example <c>{ "forward": "48 8B 0D", "within": "0x100", "offset": 3 }</c>. A number is a JSON number or a
///     <c>0x</c> hex string. An object with only <c>offset</c> is an <see cref="OffsetStep"/>.
/// </remarks>
internal sealed class RouteStepConverter : JsonConverter<RouteStep>
{
    private const string Kinds = "export, forward, backward, read and offset";
    private const int DefaultSize = 4;

    /// <summary>
    ///     Gets the word a route document spells <paramref name="kind"/> as.
    /// </summary>
    /// <param name="kind">The kind of read.</param>
    /// <returns>
    ///     The word.
    /// </returns>
    public static string NameOf(ReadKind kind)
    {
        return kind switch
        {
            ReadKind.Global => "global",
            ReadKind.Branch => "branch",
            _ => "pointer",
        };
    }

    /// <inheritdoc/>
    public override RouteStep Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"A step is an object whose key names what it does. Use {Kinds}.");
        }

        string? kind = null;
        string? value = null;
        int? within = null;
        int? size = null;
        int? offset = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string key = reader.GetString()!;
            _ = reader.Read();

            switch (key)
            {
                case "export" or "read":
                {
                    kind = SetKind(kind, key);
                    value = ReadText(ref reader, key);
                    break;
                }
                case "forward" or "backward":
                {
                    kind = SetKind(kind, key);
                    value = ReadSignature(ref reader, key);
                    break;
                }
                case "within":
                {
                    within = ReadNumber(ref reader, key);
                    break;
                }
                case "size":
                {
                    size = ReadNumber(ref reader, key);
                    break;
                }
                case "offset":
                {
                    offset = ReadNumber(ref reader, key);
                    break;
                }
                default:
                {
                    throw new JsonException($"'{key}' isn't a step or a step setting. Use {Kinds}, with within, size and offset.");
                }
            }
        }

        kind ??= offset is not null ? "offset" : throw new JsonException($"A step needs 1 key that names what it does. Use {Kinds}.");

        Disallow(kind, "within", within is not null && kind is not ("forward" or "backward"));
        Disallow(kind, "size", size is not null && kind is not "read");

        return kind switch
        {
            "export" => new ExportStep(value!, offset ?? 0),
            "forward" or "backward" => new SearchStep(CreateScan(kind, value!, within), offset ?? 0),
            "read" => new ReadStep(ParseReadKind(value!, size), size ?? DefaultSize, offset ?? 0),
            _ => new OffsetStep(offset!.Value),
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RouteStep value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        switch (value)
        {
            case ExportStep export:
            {
                writer.WriteString("export", export.Name);
                break;
            }
            case SearchStep search:
            {
                writer.WriteString(search.Search.Kind == ScanStepKind.Backward ? "backward" : "forward", search.Search.Pattern.ToString());
                if (!search.IsWholeModule)
                {
                    WriteNumber(writer, "within", search.Search.Window);
                }

                break;
            }
            case ReadStep read:
            {
                writer.WriteString("read", NameOf(read.Kind));
                if (read.Kind != ReadKind.Pointer && read.Size != DefaultSize)
                {
                    WriteNumber(writer, "size", read.Size);
                }

                break;
            }
            default:
            {
                break;
            }
        }

        if (value.Offset != 0 || value is OffsetStep)
        {
            WriteNumber(writer, "offset", value.Offset);
        }

        writer.WriteEndObject();
    }

    private static string SetKind(string? kind, string key)
    {
        return kind is null
            ? key
            : throw new JsonException($"A step does 1 thing, but this one is both '{kind}' and '{key}'. Write them as 2 steps.");
    }

    private static void Disallow(string kind, string key, bool present)
    {
        if (present)
        {
            throw new JsonException($"The '{kind}' step doesn't take '{key}'.");
        }
    }

    private static ScanStep CreateScan(string kind, string signature, int? within)
    {
        if (!ScanPattern.TryParse(signature, out var pattern))
        {
            throw new JsonException($"'{signature}' of the '{kind}' step isn't a signature. Write hex pairs, as in '48 8B ?? 05'.");
        }

        if (within is null)
        {
            return Scan.For(pattern);
        }

        if (within <= 0)
        {
            throw new JsonException($"'within' of the '{kind}' step is {within}. Write a positive size, or leave it out to search the whole module.");
        }

        return kind == "backward" ? Scan.Backward(within.Value).For(pattern) : Scan.Forward(within.Value).For(pattern);
    }

    private static ReadKind ParseReadKind(string value, int? size)
    {
        var kind = value switch
        {
            "global" => ReadKind.Global,
            "branch" => ReadKind.Branch,
            "pointer" => ReadKind.Pointer,
            _ => throw new JsonException($"'{value}' isn't a read. Use global, branch or pointer."),
        };

        if (kind == ReadKind.Pointer && size is not null)
        {
            throw new JsonException("A 'pointer' read uses the target's pointer size, so it doesn't take 'size'. Only 'global' and 'branch' do.");
        }

        return kind;
    }

    private static string ReadText(ref Utf8JsonReader reader, string key)
    {
        return reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException($"'{key}' is a string.");
    }

    // A signature is 1 string or an array of pieces, so a document can put each instruction on its own line.
    private static string ReadSignature(ref Utf8JsonReader reader, string key)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString()!;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"'{key}' is a signature string or an array of its pieces.");
        }

        List<string> pieces = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            pieces.Add(ReadText(ref reader, key));
        }

        return pieces.Count > 0
            ? string.Join(" ", pieces)
            : throw new JsonException($"'{key}' is an empty array. Write the pieces of a signature.");
    }

    private static int ReadNumber(ref Utf8JsonReader reader, string key)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.TryGetInt32(out int number)
                ? number
                : throw new JsonException($"'{key}' doesn't fit a 32-bit integer.");
        }

        return reader.TokenType == JsonTokenType.String
            ? HexNumber.Parse(reader.GetString()!, $"'{key}'")
            : throw new JsonException($"'{key}' is a number or a '0x' hex string.");
    }

    // A hex string can't be negative, so a negative number is written as a JSON number.
    private static void WriteNumber(Utf8JsonWriter writer, string key, int value)
    {
        if (value < 0)
        {
            writer.WriteNumber(key, value);
        }
        else
        {
            writer.WriteString(key, "0x" + value.ToString("X", CultureInfo.InvariantCulture));
        }
    }
}
