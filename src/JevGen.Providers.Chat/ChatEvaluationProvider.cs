using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using JevGen.Providers;

namespace JevGen.Providers.Chat;

/// <summary>Configuration shared by the chat-model evaluation providers.</summary>
public abstract class ChatEvaluationOptions
{
    /// <summary>The API key used to authenticate. Never logged.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The model to use when a contract does not select one.</summary>
    public string? Model { get; set; }

    /// <summary>The endpoint to call.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>The per-request transport timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Sampling temperature. Evaluation wants determinism, so this defaults to zero.</summary>
    public double Temperature { get; set; }

    /// <summary>
    /// Whether this provider may claim <see cref="JevProviderCapabilities.Probabilities"/>.
    /// </summary>
    /// <remarks>
    /// Off by default. A chat model's self-reported distribution is not calibrated the way a
    /// purpose-built evaluation model's is, so a contract that genuinely depends on
    /// probabilities should fail here rather than silently receive weaker numbers. Turning this
    /// on is the explicit acknowledgement that approximate probabilities are acceptable.
    /// </remarks>
    public bool AllowApproximateProbabilities { get; set; }
}

/// <summary>
/// The shared implementation for running Jev contracts on a general-purpose chat model.
/// </summary>
/// <remarks>
/// Jev semantics are expressed as a JSON schema the model must satisfy. This is a faithful
/// approximation, not an equivalence: it exists so that provider-neutral contracts have
/// somewhere to run, and so that a Jev contract can fall back to a general model when its
/// primary host is unavailable.
/// </remarks>
public abstract class ChatEvaluationProvider<TOptions> : IJevProvider
    where TOptions : ChatEvaluationOptions
{
    protected ChatEvaluationProvider(HttpClient httpClient) => HttpClient = httpClient;

    protected HttpClient HttpClient { get; }

    public abstract string Name { get; }

    protected abstract TOptions Options { get; }

    protected abstract Uri DefaultBaseAddress { get; }

    protected abstract string CompletionPath { get; }

    protected abstract string DefaultModel { get; }

    public JevProviderCapabilities Capabilities
    {
        get
        {
            var capabilities =
                JevProviderCapabilities.Noul
                | JevProviderCapabilities.Choice
                | JevProviderCapabilities.Score
                | JevProviderCapabilities.MultiQuestion
                | JevProviderCapabilities.StructuredState
                | JevProviderCapabilities.ModelSelection;

            if (Options.AllowApproximateProbabilities)
            {
                capabilities |= JevProviderCapabilities.Probabilities;
            }

            return capabilities;
        }
    }

    /// <summary>Writes the provider's request body.</summary>
    protected abstract void WriteRequestBody(
        Utf8JsonWriter writer,
        JevProviderRequest request,
        string model,
        string prompt,
        string schema,
        TOptions options);

    /// <summary>Applies authentication and provider-specific headers.</summary>
    protected abstract void PrepareRequest(HttpRequestMessage message, TOptions options);

    /// <summary>Extracts the model's answer text from the provider's response body.</summary>
    protected abstract string? ExtractContent(JsonElement root);

    /// <summary>Extracts the model identifier the provider reports, when it reports one.</summary>
    protected virtual string? ExtractModel(JsonElement root)
        => root.TryGetProperty("model", out var model) ? model.GetString() : null;

    public async ValueTask<JevProviderResponse> EvaluateAsync(
        JevProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = Options;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new EvaluationAuthenticationException(
                $"No API key is configured for the '{Name}' provider.")
            {
                Provider = Name,
            };
        }

        var model = request.Model ?? options.Model ?? DefaultModel;
        var prompt = ChatEvaluationSchema.BuildPrompt(request);
        var schema = ChatEvaluationSchema.BuildSchema(request);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteRequestBody(writer, request, model, prompt, schema, options);
        }

        var baseAddress = options.BaseAddress ?? HttpClient.BaseAddress ?? DefaultBaseAddress;

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, CompletionPath))
        {
            Content = new ByteArrayContent(buffer.ToArray())
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") },
            },
        };

        PrepareRequest(message, options);

        var started = Stopwatch.GetTimestamp();
        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var requestId = ReadRequestId(response);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateFailure(response, body, requestId);
        }

        using var document = ParseBody(body, response, requestId);

        var content = ExtractContent(document.RootElement)
            ?? throw new EvaluationResponseException(
                $"Provider '{Name}' returned a response with no message content.")
            {
                Provider = Name,
                StatusCode = (int)response.StatusCode,
                RequestId = requestId,
            };

        return JevProviderResponse.Create(
            new JevProviderMetadata
            {
                Provider = Name,
                Model = ExtractModel(document.RootElement) ?? model,
                RequestId = requestId,
                Properties = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["duration"] = Stopwatch.GetElapsedTime(started),
                    ["approximateProbabilities"] = options.AllowApproximateProbabilities,
                },
            },
            ChatEvaluationParser.Parse(request, content, Name, requestId));
    }

    private JsonDocument ParseBody(string body, HttpResponseMessage response, string? requestId)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new EvaluationResponseException(
                $"Provider '{Name}' returned a response body that is not valid JSON.",
                exception)
            {
                Provider = Name,
                StatusCode = (int)response.StatusCode,
                RequestId = requestId,
            };
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            return await HttpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new EvaluationProviderException(
                $"Provider '{Name}' could not be reached: {exception.Message}",
                exception)
            {
                Provider = Name,
                StatusCode = exception.StatusCode is { } status ? (int)status : null,
            };
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EvaluationTimeoutException(
                $"Provider '{Name}' did not respond within {HttpClient.Timeout}.",
                exception)
            {
                Provider = Name,
                Timeout = HttpClient.Timeout,
            };
        }
    }

    private Exception CreateFailure(HttpResponseMessage response, string body, string? requestId)
    {
        var status = (int)response.StatusCode;
        var detail = Summarize(body);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => new EvaluationAuthenticationException(
                    $"Provider '{Name}' rejected the request credentials: {detail}")
                {
                    Provider = Name, StatusCode = status, RequestId = requestId,
                },

            HttpStatusCode.TooManyRequests
                => new EvaluationRateLimitException($"Provider '{Name}' rate limit exceeded: {detail}")
                {
                    Provider = Name,
                    StatusCode = status,
                    RequestId = requestId,
                    RetryAfter = response.Headers.RetryAfter?.Delta,
                },

            _ => new EvaluationProviderException($"Provider '{Name}' failed with {status}: {detail}")
            {
                Provider = Name, StatusCode = status, RequestId = requestId,
            },
        };
    }

    /// <summary>Trims an error body to something safe and useful for a message.</summary>
    private static string Summarize(string body)
    {
        var trimmed = body.Trim();

        if (trimmed.Length == 0)
        {
            return "no detail";
        }

        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "...";
    }

    private static string? ReadRequestId(HttpResponseMessage response)
    {
        foreach (var name in new[] { "x-request-id", "request-id", "anthropic-request-id" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>Writes the standard OpenAI-style chat messages array.</summary>
    protected static void WriteChatMessages(Utf8JsonWriter writer, string prompt)
    {
        writer.WriteStartArray("messages");

        writer.WriteStartObject();
        writer.WriteString("role", "system");
        writer.WriteString("content", ChatEvaluationSchema.SystemInstruction);
        writer.WriteEndObject();

        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteString("content", prompt);
        writer.WriteEndObject();

        writer.WriteEndArray();
    }

    /// <summary>Writes a raw JSON document as the value of a property.</summary>
    protected static void WriteRawProperty(Utf8JsonWriter writer, string name, string json)
    {
        writer.WritePropertyName(name);

        using var document = JsonDocument.Parse(json);
        document.RootElement.WriteTo(writer);
    }

    /// <summary>Reads the first text block from a content array.</summary>
    protected static string? ReadFirstText(JsonElement array, string textProperty)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();

        foreach (var element in array.EnumerateArray())
        {
            if (element.TryGetProperty(textProperty, out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
