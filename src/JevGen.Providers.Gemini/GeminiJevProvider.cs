using System.Text.Json;
using JevGen.Providers.Chat;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.Gemini;

/// <summary>Configuration for the Gemini evaluation provider.</summary>
public sealed class GeminiJevOptions : ChatEvaluationOptions
{
    /// <summary>The API version segment of the endpoint path.</summary>
    public string ApiVersion { get; set; } = "v1beta";
}

/// <summary>
/// Runs evaluation contracts on Gemini models using its JSON response schema.
/// </summary>
/// <remarks>
/// As with every chat-model provider, this approximates Jev semantics rather than replacing
/// them. Probabilities are claimed only when the application explicitly accepts approximate
/// ones.
/// </remarks>
public sealed class GeminiJevProvider(
    HttpClient httpClient,
    IOptionsMonitor<GeminiJevOptions> options)
    : ChatEvaluationProvider<GeminiJevOptions>(httpClient)
{
    /// <summary>The registered name of this provider.</summary>
    public const string ProviderName = "gemini";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override GeminiJevOptions Options => options.Get(ProviderName);

    /// <inheritdoc />
    protected override Uri DefaultBaseAddress { get; } = new("https://generativelanguage.googleapis.com/");

    /// <summary>Gemini addresses the model in the path rather than the body.</summary>
    protected override string CompletionPath
    {
        get
        {
            var providerOptions = Options;
            var model = providerOptions.Model ?? DefaultModel;
            return $"{providerOptions.ApiVersion}/models/{model}:generateContent";
        }
    }

    /// <inheritdoc />
    protected override string DefaultModel => "gemini-2.5-flash";

    /// <inheritdoc />
    protected override void WriteRequestBody(
        Utf8JsonWriter writer,
        JevProviderRequest request,
        string model,
        string prompt,
        string schema,
        GeminiJevOptions providerOptions)
    {
        writer.WriteStartObject();

        writer.WriteStartObject("systemInstruction");
        writer.WriteStartArray("parts");
        writer.WriteStartObject();
        writer.WriteString("text", ChatEvaluationSchema.SystemInstruction);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartArray("contents");
        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteStartArray("parts");
        writer.WriteStartObject();
        writer.WriteString("text", prompt);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();

        writer.WriteStartObject("generationConfig");
        writer.WriteNumber("temperature", providerOptions.Temperature);
        writer.WriteString("responseMimeType", "application/json");
        WriteRawProperty(writer, "responseSchema", schema);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    /// <inheritdoc />
    protected override void PrepareRequest(HttpRequestMessage message, GeminiJevOptions providerOptions)
        => message.Headers.TryAddWithoutValidation("x-goog-api-key", providerOptions.ApiKey);

    /// <inheritdoc />
    protected override string? ExtractContent(JsonElement root)
        => root.TryGetProperty("candidates", out var candidates)
           && candidates.ValueKind == JsonValueKind.Array
           && candidates.GetArrayLength() > 0
           && candidates[0].TryGetProperty("content", out var content)
           && content.TryGetProperty("parts", out var parts)
            ? ReadFirstText(parts, "text")
            : null;

    /// <inheritdoc />
    protected override string? ExtractModel(JsonElement root)
        => root.TryGetProperty("modelVersion", out var model) ? model.GetString() : null;
}
