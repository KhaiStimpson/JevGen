using System.Net.Http.Headers;
using JevGen.Jev;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.OpenRouter;

/// <summary>Configuration for the OpenRouter Jev provider.</summary>
public sealed class OpenRouterJevOptions : JevOptions
{
    /// <summary>
    /// The application URL OpenRouter attributes the request to. OpenRouter uses it for
    /// rankings and abuse handling.
    /// </summary>
    public Uri? SiteUrl { get; set; }

    /// <summary>The application name OpenRouter attributes the request to.</summary>
    public string? SiteName { get; set; }

    /// <summary>
    /// Upstream providers OpenRouter should prefer, in order. Left empty, OpenRouter routes by
    /// its own policy.
    /// </summary>
    public IList<string> ProviderOrder { get; } = [];

    /// <summary>Whether OpenRouter may route to a provider outside <see cref="ProviderOrder"/>.</summary>
    public bool AllowFallbacks { get; set; } = true;
}

/// <summary>
/// The first-party adapter for Jev hosted through OpenRouter.
/// </summary>
/// <remarks>
/// Application contracts are unchanged between this provider and the direct TypeSafe one. What
/// differs is authentication, the model identifier OpenRouter expects, its extra attribution
/// headers and its routing metadata.
/// </remarks>
public sealed class OpenRouterJevProvider(
    HttpClient httpClient,
    IOptionsMonitor<OpenRouterJevOptions> options)
    : HttpJevProvider<OpenRouterJevOptions>(httpClient, options)
{
    /// <summary>The registered name of this provider.</summary>
    public const string ProviderName = "openrouter";

    /// <summary>
    /// OpenRouter's floating alias for the newest Jev build. The leading tilde is OpenRouter's
    /// marker for an alias rather than a version.
    /// </summary>
    public const string LatestModel = "~typesafe/jev-latest";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override Uri DefaultBaseAddress { get; } = new("https://openrouter.ai/api/");

    /// <inheritdoc />
    /// <remarks>
    /// Jev is a decisions model, not a chat model. OpenRouter serves it on a dedicated endpoint
    /// and rejects it on <c>/api/v1/chat/completions</c>:
    /// <c>"typesafe/jev-1.13 is a decisions model and cannot be used with the chat/completions
    /// endpoint. Use the /api/alpha/decisions endpoint instead."</c> The model is also absent
    /// from the default <c>/api/v1/models</c> listing, whose modality is <c>text-&gt;decisions</c>.
    /// </remarks>
    protected override string EvaluatePath => "alpha/decisions";

    /// <summary>Translates a JevGen model alias into the OpenRouter-hosted Jev identifier.</summary>
    /// <remarks>
    /// OpenRouter namespaces Jev under its publisher and marks the floating alias with a leading
    /// tilde: <c>~typesafe/jev-latest</c> tracks the newest build, <c>typesafe/jev-1.13</c> is a
    /// version, and <c>typesafe/jev-1.13-20260917</c> a pinned build. Anything else is passed
    /// through untouched, so a pin can be named directly.
    /// </remarks>
    /// <exception cref="EvaluationProviderException">
    /// A latency or quality tier was asked for. OpenRouter lists no such variant, and sending an
    /// invented identifier would fail as an opaque 404.
    /// </exception>
    protected override string ResolveModel(string? requested) => (requested ?? Options.Model) switch
    {
        JevModel.Latest => LatestModel,

        JevModel.Fast or JevModel.Pro => throw new EvaluationProviderException(
            $"OpenRouter does not host a '{requested ?? Options.Model}' variant of Jev. It serves " +
            $"'{LatestModel}' and pinned builds such as 'typesafe/jev-1.13-20260917'. Use " +
            $"{nameof(JevModel)}.{nameof(JevModel.Latest)}, or name a build explicitly.")
        {
            Provider = ProviderName,
        },

        var explicitModel => explicitModel,
    };

    /// <inheritdoc />
    protected override void PrepareRequest(HttpRequestMessage request, OpenRouterJevOptions options)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        if (options.SiteUrl is { } siteUrl)
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", siteUrl.ToString());
        }

        if (options.SiteName is { Length: > 0 } siteName)
        {
            request.Headers.TryAddWithoutValidation("X-Title", siteName);
        }

        if (options.ProviderOrder.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "X-OpenRouter-Provider-Order",
                string.Join(',', options.ProviderOrder));
        }

        if (!options.AllowFallbacks)
        {
            request.Headers.TryAddWithoutValidation("X-OpenRouter-Allow-Fallbacks", "false");
        }
    }

    /// <summary>Captures OpenRouter's routing detail without changing the core result contracts.</summary>
    /// <remarks>
    /// The body is the reliable source: <c>provider</c> names the upstream host that answered and
    /// <c>usage.cost</c> prices the call, both of which the base class has already recorded under
    /// their neutral names. They are mirrored under the <c>openrouter.</c> prefix for callers that
    /// read routing detail by host, and the equivalent headers still win when OpenRouter sends
    /// them.
    /// </remarks>
    protected override void CollectMetadata(
        HttpResponseMessage response,
        JevResponsePayload body,
        IDictionary<string, object?> properties)
    {
        if (body.Provider is { Length: > 0 } upstream)
        {
            properties["openrouter.provider"] = upstream;
        }

        if (body.Model is { Length: > 0 } model)
        {
            properties["openrouter.model"] = model;
        }

        if (body.Usage?.Cost is { } cost)
        {
            properties["openrouter.cost"] = cost;
        }

        foreach (var (header, key) in new[]
                 {
                     ("x-openrouter-provider", "openrouter.provider"),
                     ("x-openrouter-model", "openrouter.model"),
                     ("x-openrouter-cost", "openrouter.cost"),
                 })
        {
            if (response.Headers.TryGetValues(header, out var values)
                && values.FirstOrDefault() is { Length: > 0 } value)
            {
                properties[key] = value;
            }
        }
    }

    /// <inheritdoc />
    protected override string? ReadRequestId(HttpResponseMessage response)
        => response.Headers.TryGetValues("x-openrouter-request-id", out var values)
            ? values.FirstOrDefault()
            : base.ReadRequestId(response);
}
