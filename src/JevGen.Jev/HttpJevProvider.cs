using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JevGen.Providers;
using Microsoft.Extensions.Options;

namespace JevGen.Jev;

/// <summary>
/// The shared HTTP implementation of a Jev-protocol provider.
/// </summary>
/// <remarks>
/// Subclasses supply authentication, addressing and model naming. Everything about Jev
/// semantics — request shape, answer mapping, capability declaration — is shared, which is why
/// the same contract runs unchanged across every first-party host.
/// </remarks>
public abstract class HttpJevProvider<TOptions> : IJevProvider, IJevProviderHealth
    where TOptions : JevOptions
{
    private readonly IOptionsMonitor<TOptions> _options;

    /// <summary>Creates the provider.</summary>
    protected HttpJevProvider(HttpClient httpClient, IOptionsMonitor<TOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        HttpClient = httpClient;
        _options = options;
    }

    /// <summary>The transport used for provider calls.</summary>
    protected HttpClient HttpClient { get; }

    /// <summary>The current options.</summary>
    protected TOptions Options => _options.Get(Name);

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <summary>The endpoint used when the options do not set one.</summary>
    protected abstract Uri DefaultBaseAddress { get; }

    /// <summary>The path of the evaluation endpoint, relative to the base address.</summary>
    protected virtual string EvaluatePath => "v1/evaluate";

    /// <inheritdoc />
    /// <remarks>
    /// Every first-party Jev host supports the full semantic surface. Streaming and native
    /// batching are not claimed because the evaluation endpoint exposes neither.
    /// </remarks>
    public virtual JevProviderCapabilities Capabilities =>
        JevProviderCapabilities.Noul
        | JevProviderCapabilities.Choice
        | JevProviderCapabilities.Score
        | JevProviderCapabilities.Probabilities
        | JevProviderCapabilities.MultiQuestion
        | JevProviderCapabilities.StructuredState
        | JevProviderCapabilities.ModelSelection;

    /// <summary>Translates a JevGen model alias into this host's identifier for it.</summary>
    protected virtual string ResolveModel(string? requested) => requested ?? Options.Model;

    /// <summary>Applies authentication and any host-specific headers to a request.</summary>
    protected abstract void PrepareRequest(HttpRequestMessage request, TOptions options);

    /// <summary>Collects host-specific response detail, such as routing information.</summary>
    protected virtual void CollectMetadata(
        HttpResponseMessage response,
        IDictionary<string, object?> properties)
    {
    }

    /// <inheritdoc />
    public async ValueTask<JevProviderResponse> EvaluateAsync(
        JevProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = Options;
        options.Validate(Name);

        var payload = JevProtocol.ToPayload(request, ResolveModel(request.Model));

        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUri(options))
        {
            Content = JsonContent.Create(payload, JevJsonContext.Default.JevRequestPayload),
        };

        foreach (var header in options.DefaultHeaders)
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        ApplyProviderOptions(message, request);
        PrepareRequest(message, options);

        var started = Stopwatch.GetTimestamp();

        using var response = await SendAsync(message, cancellationToken).ConfigureAwait(false);

        var requestId = ReadRequestId(response);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateFailureAsync(response, requestId, cancellationToken).ConfigureAwait(false);
        }

        JevResponsePayload? body;

        try
        {
            body = await response.Content
                .ReadFromJsonAsync(JevJsonContext.Default.JevResponsePayload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new EvaluationResponseException(
                $"Provider '{Name}' returned a response body that is not valid Jev JSON.",
                exception)
            {
                Provider = Name,
                StatusCode = (int)response.StatusCode,
                RequestId = requestId,
            };
        }

        if (body is null)
        {
            throw new EvaluationResponseException($"Provider '{Name}' returned an empty response body.")
            {
                Provider = Name,
                StatusCode = (int)response.StatusCode,
                RequestId = requestId,
            };
        }

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["duration"] = Stopwatch.GetElapsedTime(started),
        };

        CollectMetadata(response, properties);

        return JevProviderResponse.Create(
            new JevProviderMetadata
            {
                Provider = Name,
                Model = body.Model ?? payload.Model,
                RequestId = body.Id ?? requestId,
                Properties = properties,
            },
            JevProtocol.ToResults(request, body, Name));
    }

    /// <summary>Sends the request, mapping transport failures onto the JevGen error model.</summary>
    protected virtual async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            return await HttpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
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

    private Uri BuildUri(TOptions options)
    {
        var baseAddress = options.BaseAddress ?? HttpClient.BaseAddress ?? DefaultBaseAddress;
        return new Uri(baseAddress, EvaluatePath);
    }

    private static void ApplyProviderOptions(HttpRequestMessage message, JevProviderRequest request)
    {
        // Provider-scoped extension data arrives already filtered to this provider, so a header
        // declared for one host can never leak into a call to another.
        foreach (var option in request.ProviderOptions)
        {
            if (option.Key.StartsWith("header:", StringComparison.OrdinalIgnoreCase)
                && option.Value is string value)
            {
                message.Headers.TryAddWithoutValidation(option.Key["header:".Length..], value);
            }
        }
    }

    /// <summary>Maps an error response onto the JevGen error model.</summary>
    protected async Task<Exception> CreateFailureAsync(
        HttpResponseMessage response,
        string? requestId,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = JevProtocol.TryParseError(body)?.Error?.Message ?? response.ReasonPhrase ?? "no detail";
        var status = (int)response.StatusCode;

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => new EvaluationAuthenticationException(
                    $"Provider '{Name}' rejected the request credentials: {detail}")
                {
                    Provider = Name,
                    StatusCode = status,
                    RequestId = requestId,
                },

            HttpStatusCode.TooManyRequests
                => new EvaluationRateLimitException(
                    $"Provider '{Name}' rate limit exceeded: {detail}")
                {
                    Provider = Name,
                    StatusCode = status,
                    RequestId = requestId,
                    RetryAfter = response.Headers.RetryAfter?.Delta,
                },

            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout
                => new EvaluationTimeoutException($"Provider '{Name}' timed out: {detail}")
                {
                    Provider = Name,
                    StatusCode = status,
                    RequestId = requestId,
                },

            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                => new EvaluationResponseException(
                    $"Provider '{Name}' rejected the request as invalid: {detail}")
                {
                    Provider = Name,
                    StatusCode = status,
                    RequestId = requestId,
                },

            _ => new EvaluationProviderException($"Provider '{Name}' failed with {status}: {detail}")
            {
                Provider = Name,
                StatusCode = status,
                RequestId = requestId,
            },
        };
    }

    /// <summary>Reads the host's request-identifier header, when it sets one.</summary>
    protected virtual string? ReadRequestId(HttpResponseMessage response)
    {
        foreach (var name in new[] { "x-request-id", "request-id", "x-jev-request-id" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    /// <inheritdoc />
    public virtual ValueTask<JevProviderHealthResult> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            Options.Validate(Name);
        }
        catch (EvaluationAuthenticationException exception)
        {
            return new ValueTask<JevProviderHealthResult>(
                JevProviderHealthResult.Unhealthy(exception.Message, exception));
        }

        return new ValueTask<JevProviderHealthResult>(
            JevProviderHealthResult.Healthy($"Provider '{Name}' is configured."));
    }
}
