using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace JevGen;

/// <summary>
/// Serialization metadata for the composite state generated clients build when a method
/// combines a state parameter with named context parameters.
/// </summary>
/// <remarks>
/// Composing state into a pre-serialized <see cref="JsonElement"/> map keeps the whole path
/// reflection-free: each part is serialized with its own metadata, and the envelope is
/// described by JevGen's own source-generated context.
/// </remarks>
public static class JevCompositeState
{
    /// <summary>Serialization metadata for the composite state envelope.</summary>
    public static JsonTypeInfo<Dictionary<string, JsonElement>> TypeInfo
        => JevCompositeStateJsonContext.Default.DictionaryStringJsonElement;

    /// <summary>
    /// Serializes one part of a composite state, preferring supplied metadata over reflection.
    /// </summary>
    /// <exception cref="EvaluationSerializationException">The value could not be serialized.</exception>
    public static JsonElement Part<T>(T value)
    {
        var typeInfo = JevGenJson.TryGetTypeInfo(typeof(T));

        try
        {
            return typeInfo is not null
                ? JsonSerializer.SerializeToElement(value, typeInfo)
                : throw new EvaluationSerializationException(
                    $"No JSON serialization metadata is available for the context value type '{typeof(T)}'. " +
                    "Declare a JsonSerializerContext covering it and point JevGen at the context with " +
                    "[assembly: JevJsonContext(typeof(YourContext))].");
        }
        catch (JsonException exception)
        {
            throw new EvaluationSerializationException(
                $"A composite state part of type '{typeof(T)}' could not be serialized.",
                exception);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class JevCompositeStateJsonContext : JsonSerializerContext;
