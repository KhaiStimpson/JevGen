namespace JevGen;

/// <summary>How a contract/provider capability mismatch is handled.</summary>
public enum CapabilityValidationMode
{
    /// <summary>
    /// Fail. A contract that requires semantics its provider cannot honour is a configuration
    /// error, and is reported as one at start-up. This is the default.
    /// </summary>
    Fail = 0,

    /// <summary>
    /// Log a warning and continue. Only meaningful when the application has explicitly decided
    /// that degraded results are acceptable for the contracts involved.
    /// </summary>
    Warn = 1,
}

/// <summary>Global JevGen configuration.</summary>
public sealed class JevGenOptions
{
    /// <summary>
    /// The model used when neither the contract nor a method selects one and the provider has
    /// no default of its own.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// The registered provider name used when nothing overrides the selection. When unset the
    /// first registered provider is used.
    /// </summary>
    public string? DefaultProvider { get; set; }

    /// <summary>The time budget applied to a whole evaluation, including retries and fallbacks.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to attach <see cref="EvaluationMetadata"/> (provider, model, request identifier,
    /// duration) to results.
    /// </summary>
    public bool RecordMetadata { get; set; } = true;

    /// <summary>How capability mismatches are handled. Defaults to failing.</summary>
    public CapabilityValidationMode CapabilityValidation { get; set; } = CapabilityValidationMode.Fail;

    /// <summary>
    /// Whether to validate every registered contract against its provider when the application
    /// starts, rather than on first use.
    /// </summary>
    public bool ValidateOnStart { get; set; } = true;

    /// <summary>Per-contract configuration, keyed by the contract type's full name.</summary>
    public IDictionary<string, JevClientConfiguration> Clients { get; }
        = new Dictionary<string, JevClientConfiguration>(StringComparer.Ordinal);

    /// <summary>Returns the configuration for a contract, creating it on first use.</summary>
    public JevClientConfiguration ClientFor(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        var key = contractType.FullName ?? contractType.Name;

        if (!Clients.TryGetValue(key, out var configuration))
        {
            configuration = new JevClientConfiguration();
            Clients[key] = configuration;
        }

        return configuration;
    }
}

/// <summary>
/// Per-contract configuration. Values set here are explicit programmatic configuration and
/// take precedence over the equivalent attributes on the contract.
/// </summary>
public sealed class JevClientConfiguration
{
    /// <summary>The registered provider name this contract should use.</summary>
    public string? Provider { get; set; }

    /// <summary>
    /// The provider implementation type this contract should use, when it was selected by type
    /// rather than by name.
    /// </summary>
    /// <remarks>
    /// The name is resolved from the registered instance when the container is available, so
    /// selection never depends on reading a name reflectively.
    /// </remarks>
    public Type? ProviderType { get; set; }

    /// <summary>The model this contract should use.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Providers to try, in order, when the primary provider cannot serve the request.
    /// </summary>
    public IList<string> FallbackProviders { get; } = [];

    /// <summary>Fallback providers selected by implementation type rather than by name.</summary>
    public IList<Type> FallbackProviderTypes { get; } = [];

    /// <summary>
    /// When set, a result whose lowest confidence falls below this threshold causes the next
    /// fallback provider to be tried. The highest-confidence result is returned.
    /// </summary>
    public double? FallbackWhenConfidenceBelow { get; set; }

    /// <summary>How capability mismatches are handled for this contract.</summary>
    public CapabilityValidationMode? CapabilityValidation { get; set; }

    /// <summary>
    /// Provider-specific extension data, keyed by provider name. Entries only ever reach the
    /// provider they name.
    /// </summary>
    public IDictionary<string, IDictionary<string, object?>> ProviderOptions { get; }
        = new Dictionary<string, IDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the option bag for a provider, creating it on first use.</summary>
    public IDictionary<string, object?> ProviderOptionsFor(string provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (!ProviderOptions.TryGetValue(provider, out var options))
        {
            options = new Dictionary<string, object?>(StringComparer.Ordinal);
            ProviderOptions[provider] = options;
        }

        return options;
    }
}
