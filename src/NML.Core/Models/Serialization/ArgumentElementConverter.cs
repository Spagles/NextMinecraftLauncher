using System.Text.Json;
using System.Text.Json.Serialization;
using NML.Core.Rules;

namespace NML.Core.Models.Serialization;

/// <summary>
/// Reads an <see cref="ArgumentElement"/> from JSON where the value is either a
/// plain JSON string or an object <c>{ "value": "..." | [...], "rules": [...] }</c>.
/// </summary>
public sealed class ArgumentElementConverter : JsonConverter<ArgumentElement>
{
    public override ArgumentElement? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return ArgumentElement.FromLiteral(reader.GetString() ?? string.Empty);

            case JsonTokenType.StartObject:
            {
                List<string> values = new();
                List<Rule>? rules = null;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    string prop = reader.GetString() ?? string.Empty;
                    reader.Read();

                    if (prop == "value")
                    {
                        if (reader.TokenType == JsonTokenType.String)
                            values.Add(reader.GetString() ?? string.Empty);
                        else if (reader.TokenType == JsonTokenType.StartArray)
                        {
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            {
                                if (reader.TokenType == JsonTokenType.String)
                                    values.Add(reader.GetString() ?? string.Empty);
                                else
                                    reader.Skip();
                            }
                        }
                        else
                        {
                            reader.Skip();
                        }
                    }
                    else if (prop == "rules")
                    {
                        var deserialized = JsonSerializer.Deserialize<List<Rule>>(ref reader, options);
                        rules = deserialized;
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                return ArgumentElement.FromConditional(
                    values,
                    rules ?? new List<Rule>());
            }

            default:
                throw new JsonException(
                    $"Expected string or object for ArgumentElement, got {reader.TokenType}.");
        }
    }

    public override void Write(
        Utf8JsonWriter writer, ArgumentElement value, JsonSerializerOptions options)
    {
        if (!value.IsConditional)
        {
            writer.WriteStringValue(value.Literal);
            return;
        }

        writer.WriteStartObject();
        if (value.Values!.Count == 1)
            writer.WriteString("value", value.Values[0]);
        else
        {
            writer.WriteStartArray("value");
            foreach (string v in value.Values)
                writer.WriteStringValue(v);
            writer.WriteEndArray();
        }

        if (value.Rules is { Count: > 0 })
        {
            writer.WritePropertyName("rules");
            JsonSerializer.Serialize(writer, value.Rules, options);
        }
        writer.WriteEndObject();
    }
}
