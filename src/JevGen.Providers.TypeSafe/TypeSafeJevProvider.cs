using System.Net.Http.Headers;
using JevGen.Jev;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.TypeSafe;

/// <summary>Configuration for the TypeSafe Jev provider.</summary>
public sealed class TypeSafeJevOptions : JevOptions
{
    /// <summary>The TypeSafe organisation to bill and attribute the request to.</summary>
    public string? Organization { get; set; }
}

/// <summary>
/// The first-party adapter for calling the TypeSafe Jev API directly.
/// </summary>
/// <remarks>
/// Hosting is separate from semantics: a contract written against Jev runs identically here,
/// through OpenRouter, or against an internal gateway. Only authentication, addressing and
/// model naming differ, and all three live in this class.
/// </remarks>
public sealed class TypeSafeJevProvider(
    HttpClient httpClient,
    IOptionsMonitor<TypeSafeJevOptions> options)
    : HttpJevProvider<TypeSafeJevOptions>(httpClient, options)
{
    /// <summary>The registered name of this provider.</summary>
    public const string ProviderName = "typesafe";

    /// <inheritdoc />
    public override string Name => ProviderName;

    /// <inheritdoc />
    protected override Uri DefaultBaseAddress { get; } = new("https://api.typesafe.ai/");

    /// <inheritdoc />
    protected override string EvaluatePath => "v1/jev/evaluate";

    /// <inheritdoc />
    protected override string ResolveModel(string? requested) => (requested ?? Options.Model) switch
    {
        JevModel.Latest => "jev-1",
        JevModel.Fast => "jev-1-fast",
        JevModel.Pro => "jev-1-pro",
        var explicitModel => explicitModel,
    };

    /// <inheritdoc />
    protected override void PrepareRequest(HttpRequestMessage request, TypeSafeJevOptions options)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        if (options.Organization is { Length: > 0 } organization)
        {
            request.Headers.TryAddWithoutValidation("TypeSafe-Organization", organization);
        }
    }

    /// <inheritdoc />
    protected override void CollectMetadata(
        HttpResponseMessage response,
        IDictionary<string, object?> properties)
    {
        if (response.Headers.TryGetValues("typesafe-model-version", out var version))
        {
            properties["modelVersion"] = version.FirstOrDefault();
        }
    }
}
