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
/// <remarks>
/// <strong>Unverified against the live service.</strong> The endpoint and model naming here are
/// taken from the source of the official <c>typesafe_sdk</c> 0.7.0 Python package, whose
/// <c>prepare_system_one</c> posts <c>{state, model, questions}</c> to <c>/v1/systemone</c> on
/// <c>https://api.typesafe.ai</c>. Reaching the API to confirm it needs an early-access key.
/// The OpenRouter provider, by contrast, is verified against the live decisions endpoint.
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
    /// <remarks>
    /// This is the path the official <c>typesafe_sdk</c> builds for System One, read from its
    /// source at version 0.7.0. <strong>It has not been confirmed against the live service</strong>,
    /// which needs an early-access key.
    /// </remarks>
    protected override string EvaluatePath => "v1/systemone";

    /// <summary>Translates a JevGen model alias into TypeSafe's identifier for it.</summary>
    /// <remarks>
    /// TypeSafe names models in its own namespace, not OpenRouter's: <c>jev-latest</c> is the
    /// SDK's own default and is already what the API expects, so the alias passes straight
    /// through. <c>GET /v1/models</c> lists what else is accepted.
    /// </remarks>
    /// <exception cref="EvaluationProviderException">
    /// A latency or quality tier was asked for. TypeSafe publishes no such variant.
    /// </exception>
    protected override string ResolveModel(string? requested) => (requested ?? Options.Model) switch
    {
        JevModel.Latest => "jev-latest",

        JevModel.Fast or JevModel.Pro => throw new EvaluationProviderException(
            $"TypeSafe does not publish a '{requested ?? Options.Model}' variant of Jev. Use " +
            $"{nameof(JevModel)}.{nameof(JevModel.Latest)}, or name a model from GET /v1/models.")
        {
            Provider = ProviderName,
        },

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
        JevResponsePayload body,
        IDictionary<string, object?> properties)
    {
        if (response.Headers.TryGetValues("typesafe-model-version", out var version))
        {
            properties["modelVersion"] = version.FirstOrDefault();
        }
    }

    /// <inheritdoc />
    protected override string? ReadRequestId(HttpResponseMessage response)
        => response.Headers.TryGetValues("x-typesafe-request-id", out var values)
            ? values.FirstOrDefault()
            : base.ReadRequestId(response);
}
