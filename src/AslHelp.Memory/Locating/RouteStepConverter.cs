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
    private const int DefaultSize = 4;

    /// <summary>
    ///     Gets the word a route document spells <paramref name="kind"/> as.
    /// </summary>
    /// <param name="kind">The kind of read.</param>
    /// <returns>
    ///     The word.
    /// </returns>
    public static string GetName(ReadKind kind)
    {
        return kind switch
        {
            ReadKind.Global => "global",
            ReadKind.Displacement => "displacement",
            _ => "pointer",
        };
    }

    /// <inheritdoc/>
    public override RouteStep Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(SR.StepNotObject);
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
                    kind = SetKindOnce(kind, key);
                    value = ReadText(ref reader, key);
                    break;
                }
                case "forward" or "backward":
                {
                    kind = SetKindOnce(kind, key);
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
                    throw new JsonException(SR.FormatStepKeyUnknown(key));
                }
            }
        }

        kind ??= offset is not null ? "offset" : throw new JsonException(SR.StepKindMissing);

        ThrowIfPresent(kind, "within", within is not null && kind is not ("forward" or "backward"));
        ThrowIfPresent(kind, "size", size is not null && kind is not "read");

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
                writer.WriteString(search.Scan.Kind == ScanStepKind.Backward ? "backward" : "forward", search.Scan.Pattern.ToString());
                if (!search.IsWholeModule)
                {
                    WriteNumber(writer, "within", search.Scan.WindowSize);
                }

                break;
            }
            case ReadStep read:
            {
                writer.WriteString("read", GetName(read.Kind));
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

    private static string SetKindOnce(string? kind, string key)
    {
        return kind is null
            ? key
            : throw new JsonException(SR.FormatStepHasTwoKinds(kind, key));
    }

    private static void ThrowIfPresent(string kind, string key, bool present)
    {
        if (present)
        {
            throw new JsonException(SR.FormatStepKeyNotTaken(kind, key));
        }
    }

    private static ScanStep CreateScan(string kind, string signature, int? within)
    {
        if (!ScanPattern.TryParse(signature, out var pattern))
        {
            throw new JsonException(SR.FormatStepSignatureInvalid(signature, kind));
        }

        if (within is null)
        {
            return Scan.Create(pattern);
        }

        if (within <= 0)
        {
            throw new JsonException(SR.FormatStepWithinInvalid(kind, within));
        }

        return kind == "backward" ? ScanWindow.Backward(within.Value).Create(pattern) : ScanWindow.Forward(within.Value).Create(pattern);
    }

    private static ReadKind ParseReadKind(string value, int? size)
    {
        var kind = value switch
        {
            "global" => ReadKind.Global,
            "displacement" => ReadKind.Displacement,
            "pointer" => ReadKind.Pointer,
            _ => throw new JsonException(SR.FormatReadKindUnknown(value)),
        };

        if (kind == ReadKind.Pointer && size is not null)
        {
            throw new JsonException(SR.PointerReadTakesNoSize);
        }

        return kind;
    }

    private static string ReadText(ref Utf8JsonReader reader, string key)
    {
        return reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException(SR.FormatKeyNotString(key));
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
            throw new JsonException(SR.FormatKeyNotSignature(key));
        }

        List<string> pieces = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            pieces.Add(ReadText(ref reader, key));
        }

        return pieces.Count > 0
            ? string.Join(" ", pieces)
            : throw new JsonException(SR.FormatKeyEmptyArray(key));
    }

    private static int ReadNumber(ref Utf8JsonReader reader, string key)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.TryGetInt32(out int number)
                ? number
                : throw new JsonException(SR.FormatKeyNotInt32(key));
        }

        return reader.TokenType == JsonTokenType.String
            ? HexNumber.Parse(reader.GetString()!, $"'{key}'")
            : throw new JsonException(SR.FormatKeyNotNumber(key));
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
