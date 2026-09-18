using JevGen.Providers;

namespace JevGen;

/// <summary>
/// Presents a provider under a different registered name.
/// </summary>
/// <remarks>
/// This is what lets one implementation be registered more than once with different
/// configuration — a gateway pointed at two environments, say — while each registration is
/// selectable by its own name.
/// </remarks>
public sealed class NamedJevProvider : IJevProvider, IJevProviderHealth
{
    /// <summary>Wraps a provider under a new name.</summary>
    public NamedJevProvider(string name, IJevProvider inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(inner);

        Name = name;
        Inner = inner;
    }

    /// <summary>The provider being presented under a different name.</summary>
    public IJevProvider Inner { get; }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public JevProviderCapabilities Capabilities => Inner.Capabilities;

    /// <inheritdoc />
    public ValueTask<JevProviderResponse> EvaluateAsync(
        JevProviderRequest request,
        CancellationToken cancellationToken = default)
        => Inner.EvaluateAsync(request, cancellationToken);

    /// <inheritdoc />
    public ValueTask<JevProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        => Inner is IJevProviderHealth health
            ? health.CheckHealthAsync(cancellationToken)
            : new ValueTask<JevProviderHealthResult>(
                JevProviderHealthResult.Healthy("No health probe is implemented."));
}
