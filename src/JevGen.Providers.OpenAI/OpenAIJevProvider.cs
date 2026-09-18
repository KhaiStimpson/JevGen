using System.Net.Http.Headers;
using System.Text.Json;
using JevGen.Providers.Chat;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.OpenAI;

/// <summary>Configuration for the OpenAI evaluation provider.</summary>
public sealed class OpenAIJevOptions : ChatEvaluationOptions
{
    /// <summary>The OpenAI organisation to attribute the request to.</summary>
    public string? Organization { get; set; }
}

/// <summary>
/// Runs evaluation contracts on OpenAI models using strict structured output.
/// </summary>
/// <remarks>
/// This is an approximation of Jev semantics on a general-purpose model, offered so that
/// provider-neutral contracts have somewhere to run and so that a Jev contract can fail over
/// to a general model. Probabilities are only claimed when the application opts in, because a
/// chat model's self-reported distribution is not calibrated the way Jev's is.
/// </remarks>
public sealed class OpenAIJevProvider(
    HttpClient httpClient,
    IOptionsMonitor<OpenAIJevOptions> options)
    : ChatEvaluationProvider<OpenAIJevOptions>(httpClient)
{
    /// <summary>The registered name of this provider.</summary>
    public const string ProviderName = "openai";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override OpenAIJevOptions Options => options.Get(ProviderName);

    /// <inheritdoc />
    protected override Uri DefaultBaseAddress { get; } = new("https://api.openai.com/");

    /// <inheritdoc />
    protected override string CompletionPath => "v1/chat/completions";

    /// <inheritdoc />
    protected override string DefaultModel => "gpt-4o-2024-08-06";

    /// <inheritdoc />
    protected override void WriteRequestBody(
        Utf8JsonWriter writer,
        JevProviderRequest request,
        string model,
        string prompt,
        string schema,
        OpenAIJevOptions providerOptions)
    {
        writer.WriteStartObject();
        writer.WriteString("model", model);
        writer.WriteNumber("temperature", providerOptions.Temperature);
        WriteChatMessages(writer, prompt);

        writer.WriteStartObject("response_format");
        writer.WriteString("type", "json_schema");
        writer.WriteStartObject("json_schema");
        writer.WriteString("name", "jev_evaluation");
        writer.WriteBoolean("strict", true);
        WriteRawProperty(writer, "schema", schema);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    /// <inheritdoc />
    protected override void PrepareRequest(HttpRequestMessage message, OpenAIJevOptions providerOptions)
    {
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerOptions.ApiKey);

        if (providerOptions.Organization is { Length: > 0 } organization)
        {
            message.Headers.TryAddWithoutValidation("OpenAI-Organization", organization);
        }
    }

    /// <inheritdoc />
    protected override string? ExtractContent(JsonElement root)
        => root.TryGetProperty("choices", out var choices)
           && choices.ValueKind == JsonValueKind.Array
           && choices.GetArrayLength() > 0
           && choices[0].TryGetProperty("message", out var message)
           && message.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
}
