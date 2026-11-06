using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Provides the JSON form of a <see cref="MemoryLocator"/>: an object whose keys are the route names and whose
///     values are arrays of steps.
/// </summary>
internal sealed class MemoryLocatorConverter : JsonConverter<MemoryLocator>
{
    /// <inheritdoc/>
    public override MemoryLocator Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            throw new JsonException(SR.LocatorHasSteps);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(SR.LocatorNotObject);
        }

        List<LocatorRoute> routes = [];

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            _ = reader.Read();

            routes.Add(ReadRoute(ref reader, name, options));
        }

        try
        {
            return new MemoryLocator([.. routes]);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, MemoryLocator value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        foreach (var route in value.Routes.Values)
        {
            writer.WritePropertyName(route.Name);
            JsonSerializer.Serialize(writer, route.Steps, options);
        }

        writer.WriteEndObject();
    }

    private static LocatorRoute ReadRoute(ref Utf8JsonReader reader, string name, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(SR.FormatRouteNotArray(name));
        }

        try
        {
            var steps = JsonSerializer.Deserialize<RouteStep[]>(ref reader, options);
            return new LocatorRoute(name, steps!);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new JsonException(SR.FormatRouteInvalid(name, ex.Message), ex);
        }
    }
}
