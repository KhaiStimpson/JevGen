using System.Text.Json;
using JevGen.Providers.Chat;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.Anthropic;

/// <summary>Configuration for the Anthropic evaluation provider.</summary>
public sealed class AnthropicJevOptions : ChatEvaluationOptions
{
    /// <summary>The Anthropic API version header value.</summary>
    public string ApiVersion { get; set; } = "2023-06-01";

    /// <summary>The maximum number of tokens the model may produce.</summary>
    public int MaxTokens { get; set; } = 4096;
}

/// <summary>
/// Runs evaluation contracts on Anthropic models, using a tool definition to force a
/// schema-shaped answer.
/// </summary>
/// <remarks>
/// As with every chat-model provider, this approximates Jev semantics rather than replacing
/// them. Probabilities are claimed only when the application explicitly accepts approximate
/// ones.
/// </remarks>
public sealed class AnthropicJevProvider(
    HttpClient httpClient,
    IOptionsMonitor<AnthropicJevOptions> options)
    : ChatEvaluationProvider<AnthropicJevOptions>(httpClient)
{
    /// <summary>The registered name of this provider.</summary>
    public const string ProviderName = "anthropic";

    private const string ToolName = "record_evaluation";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override AnthropicJevOptions Options => options.Get(ProviderName);

    /// <inheritdoc />
    protected override Uri DefaultBaseAddress { get; } = new("https://api.anthropic.com/");

    /// <inheritdoc />
    protected override string CompletionPath => "v1/messages";

    /// <inheritdoc />
    protected override string DefaultModel => "claude-sonnet-5";

    /// <inheritdoc />
    protected override void WriteRequestBody(
        Utf8JsonWriter writer,
        JevProviderRequest request,
        string model,
        string prompt,
        string schema,
        AnthropicJevOptions providerOptions)
    {
        writer.WriteStartObject();
        writer.WriteString("model", model);
        writer.WriteNumber("max_tokens", providerOptions.MaxTokens);
        writer.WriteNumber("temperature", providerOptions.Temperature);
        writer.WriteString("system", ChatEvaluationSchema.SystemInstruction);

        writer.WriteStartArray("messages");
        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteString("content", prompt);
        writer.WriteEndObject();
        writer.WriteEndArray();

        // A single tool with the evaluation schema, forced, is how the answer is pinned to the
        // required shape rather than left to prose parsing.
        writer.WriteStartArray("tools");
        writer.WriteStartObject();
        writer.WriteString("name", ToolName);
        writer.WriteString("description", "Record the answer to every question about the state.");
        WriteRawProperty(writer, "input_schema", schema);
        writer.WriteEndObject();
        writer.WriteEndArray();

        writer.WriteStartObject("tool_choice");
        writer.WriteString("type", "tool");
        writer.WriteString("name", ToolName);
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    /// <inheritdoc />
    protected override void PrepareRequest(HttpRequestMessage message, AnthropicJevOptions providerOptions)
    {
        message.Headers.TryAddWithoutValidation("x-api-key", providerOptions.ApiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", providerOptions.ApiVersion);
    }

    /// <inheritdoc />
    protected override string? ExtractContent(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type)
                && type.GetString() == "tool_use"
                && block.TryGetProperty("input", out var input))
            {
                return input.GetRawText();
            }
        }

        // Some responses fall back to plain text; the parser copes with fenced JSON.
        return ReadFirstText(content, "text");
    }
}
