using System.Net.Http.Headers;
using JevGen.Jev;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.Local;

/// <summary>Configuration for a self-hosted, Jev-compatible endpoint.</summary>
public sealed class LocalJevOptions : JevOptions
{
    /// <summary>
    /// Whether the endpoint requires an API key. Self-hosted endpoints on a trusted network
    /// often do not, so authentication is off by default here.
    /// </summary>
    public bool RequireApiKey { get; set; }

    /// <summary>
    /// Capabilities this endpoint supports. Declaring them accurately is what lets JevGen
    /// refuse a contract the endpoint cannot honour, instead of degrading it silently.
    /// </summary>
    public JevProviderCapabilities Capabilities { get; set; } =
        JevProviderCapabilities.Noul
        | JevProviderCapabilities.Choice
        | JevProviderCapabilities.Score
        | JevProviderCapabilities.Probabilities
        | JevProviderCapabilities.MultiQuestion
        | JevProviderCapabilities.StructuredState;

    /// <inheritdoc />
    public override void Validate(string providerName)
    {
        if (RequireApiKey)
        {
            base.Validate(providerName);
            return;
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new JevGenException(
                $"The '{providerName}' provider is configured with a non-positive timeout of {Timeout}.");
        }

        if (BaseAddress is null)
        {
            throw new JevGenException(
                $"The '{providerName}' provider needs a BaseAddress pointing at the Jev-compatible endpoint.");
        }
    }
}

/// <summary>
/// Runs Jev contracts against a self-hosted, Jev-compatible endpoint.
/// </summary>
/// <remarks>
/// The same contract that runs against TypeSafe or OpenRouter runs here unchanged, which is
/// the point of separating Jev semantics from Jev hosting.
/// </remarks>
public sealed class LocalJevProvider(
    HttpClient httpClient,
    IOptionsMonitor<LocalJevOptions> options)
    : HttpJevProvider<LocalJevOptions>(httpClient, options)
{
    /// <summary>The registered name of this provider.</summary>
    public const string ProviderName = "local";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    public override JevProviderCapabilities Capabilities => Options.Capabilities;

    /// <inheritdoc />
    protected override Uri DefaultBaseAddress { get; } = new("http://localhost:8080/");

    /// <inheritdoc />
    protected override void PrepareRequest(HttpRequestMessage request, LocalJevOptions options)
    {
        if (options.ApiKey is { Length: > 0 } apiKey)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }
}
