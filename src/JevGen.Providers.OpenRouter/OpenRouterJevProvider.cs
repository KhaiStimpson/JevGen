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

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override Uri DefaultBaseAddress { get; } = new("https://openrouter.ai/api/");

    /// <inheritdoc />
    protected override string EvaluatePath => "v1/jev/evaluate";

    /// <summary>Translates a JevGen model alias into the OpenRouter-hosted Jev identifier.</summary>
    protected override string ResolveModel(string? requested) => (requested ?? Options.Model) switch
    {
        JevModel.Latest => "typesafe/jev-1",
        JevModel.Fast => "typesafe/jev-1-fast",
        JevModel.Pro => "typesafe/jev-1-pro",
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
    protected override void CollectMetadata(
        HttpResponseMessage response,
        IDictionary<string, object?> properties)
    {
        foreach (var (header, key) in new[]
                 {
                     ("x-openrouter-provider", "openrouter.provider"),
                     ("x-openrouter-model", "openrouter.model"),
                     ("x-openrouter-cost", "openrouter.cost"),
                 })
        {
            if (response.Headers.TryGetValues(header, out var values))
            {
                properties[key] = values.FirstOrDefault();
            }
        }
    }

    /// <inheritdoc />
    protected override string? ReadRequestId(HttpResponseMessage response)
        => response.Headers.TryGetValues("x-openrouter-request-id", out var values)
            ? values.FirstOrDefault()
            : base.ReadRequestId(response);
}
