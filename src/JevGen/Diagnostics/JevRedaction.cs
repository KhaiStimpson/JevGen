using System.Collections.Immutable;
using System.Text.Json;

namespace JevGen;

/// <summary>
/// Removes properties marked <see cref="JevSensitiveAttribute"/> from state before it is
/// written to diagnostics.
/// </summary>
/// <remarks>
/// A sensitive property is still sent to the provider — it is part of what the model reasons
/// about — but it must not reach logs, traces or debug output. The property names come from
/// compile-time metadata the generator emitted, so redaction needs no reflection and works in a
/// trimmed or Native AOT application.
/// </remarks>
public static class JevRedaction
{
    /// <summary>The placeholder written in place of a redacted value.</summary>
    public const string Placeholder = "[redacted]";

    /// <summary>
    /// Renders state as JSON with every sensitive property replaced by
    /// <see cref="Placeholder"/>.
    /// </summary>
    /// <remarks>
    /// Returns the state type's name rather than throwing when the state cannot be serialized:
    /// diagnostics must never be the thing that fails an evaluation.
    /// </remarks>
    public static string Describe(EvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.StateTypeInfo is null)
        {
            return request.State.GetType().Name;
        }

        try
        {
            var element = JsonSerializer.SerializeToElement(request.State, request.StateTypeInfo);
            return Redact(element, request.SensitiveProperties);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return request.State.GetType().Name;
        }
    }

    /// <summary>Rewrites a JSON object with the named properties replaced by the placeholder.</summary>
    public static string Redact(JsonElement element, ImmutableArray<string> sensitiveProperties)
    {
        if (sensitiveProperties.IsDefaultOrEmpty || element.ValueKind != JsonValueKind.Object)
        {
            return element.GetRawText();
        }

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, element, sensitiveProperties);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element, ImmutableArray<string> sensitive)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject())
                {
                    if (IsSensitive(property.Name, sensitive))
                    {
                        writer.WriteString(property.Name, Placeholder);
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, sensitive);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    // Sensitive properties nested inside collections are redacted too.
                    Write(writer, item, sensitive);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitive(string name, ImmutableArray<string> sensitive)
    {
        foreach (var candidate in sensitive)
        {
            // The serialized name is camel-cased while the declared one is not, so compare
            // without regard to case.
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
