using System.Text.Json;
using System.Text.Json.Serialization;

namespace JevGen.Jev;

/// <summary>
/// Reads and writes <see cref="JevQuestionPayload"/> in its System One form.
/// </summary>
/// <remarks>
/// The wire has one <c>criteria</c> field whose JSON shape is chosen by the question's
/// <c>type</c>: an object for <c>choice</c>, an array for <c>score</c>, and an object of the two
/// outcome descriptions for <c>noul</c>. Keeping the three typed on the payload and collapsing
/// them here means callers cannot build a question whose criteria do not match its type, which a
/// single loosely typed field would allow.
/// </remarks>
public sealed class JevQuestionPayloadConverter : JsonConverter<JevQuestionPayload>
{
    /// <inheritdoc />
    public override JevQuestionPayload Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A Jev question must be a JSON object.");
        }

        string? type = null;
        string? instructions = null;
        var criteria = default(JsonElement);
        var hasCriteria = false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "type":
                    type = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;

                case "instructions":
                    instructions = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;

                case "criteria":
                    criteria = JsonElement.ParseValue(ref reader);
                    hasCriteria = true;
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        if (type is null)
        {
            throw new JsonException("A Jev question must declare a type.");
        }

        var payload = new JevQuestionPayload { Type = type, Instructions = instructions };

        if (!hasCriteria || criteria.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return payload;
        }

        return criteria.ValueKind switch
        {
            JsonValueKind.Array => payload with { ScoreCriteria = ReadScoreCriteria(criteria) },

            JsonValueKind.Object when string.Equals(type, "noul", StringComparison.Ordinal)
                => payload with { NoulCriteria = ReadNoulCriteria(criteria) },

            JsonValueKind.Object => payload with { ChoiceCriteria = ReadChoiceCriteria(criteria) },

            _ => throw new JsonException($"A '{type}' question has criteria of an unexpected shape."),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, JevQuestionPayload value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("type", value.Type);

        if (value.Instructions is not null)
        {
            writer.WriteString("instructions", value.Instructions);
        }

        if (value.ChoiceCriteria is { } choices)
        {
            writer.WritePropertyName("criteria");
            writer.WriteStartObject();

            foreach (var (id, description) in choices)
            {
                // A null description is meaningful upstream: the alternative is interpreted by
                // its name alone. It is written, not dropped.
                writer.WritePropertyName(id);

                if (description is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    writer.WriteStringValue(description);
                }
            }

            writer.WriteEndObject();
        }
        else if (value.ScoreCriteria is { } levels)
        {
            writer.WritePropertyName("criteria");
            writer.WriteStartArray();

            foreach (var level in levels)
            {
                writer.WriteStringValue(level);
            }

            writer.WriteEndArray();
        }
        else if (value.NoulCriteria is { } outcomes && (outcomes.True is not null || outcomes.False is not null))
        {
            writer.WritePropertyName("criteria");
            writer.WriteStartObject();

            if (outcomes.True is not null)
            {
                writer.WriteString("true", outcomes.True);
            }

            if (outcomes.False is not null)
            {
                writer.WriteString("false", outcomes.False);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static string[] ReadScoreCriteria(JsonElement criteria)
    {
        var levels = new string[criteria.GetArrayLength()];
        var index = 0;

        foreach (var level in criteria.EnumerateArray())
        {
            levels[index++] = level.ValueKind == JsonValueKind.String
                ? level.GetString()!
                : level.GetRawText();
        }

        return levels;
    }

    private static Dictionary<string, string?> ReadChoiceCriteria(JsonElement criteria)
    {
        var choices = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var choice in criteria.EnumerateObject())
        {
            choices[choice.Name] = choice.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => choice.Value.GetString(),
                _ => choice.Value.GetRawText(),
            };
        }

        return choices;
    }

    private static JevNoulCriteria ReadNoulCriteria(JsonElement criteria) => new()
    {
        True = ReadOutcome(criteria, "true"),
        False = ReadOutcome(criteria, "false"),
    };

    private static string? ReadOutcome(JsonElement criteria, string name)
        => criteria.TryGetProperty(name, out var outcome) && outcome.ValueKind == JsonValueKind.String
            ? outcome.GetString()
            : null;
}
