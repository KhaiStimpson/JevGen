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

    /// <summary>
    /// Finds the configuration for a contract without creating it.
    /// </summary>
    /// <remarks>
    /// <see cref="ClientFor"/> keys by the contract's full name, so that is tried first. Keys
    /// written by hand or bound from configuration are often the bare interface name or the
    /// <c>[JevClient(Name = ...)]</c> display name, so those still match, exactly first and then
    /// as the last segment of a namespace- or nesting-qualified key.
    /// </remarks>
    internal JevClientConfiguration? FindClient(Type? contractType, string clientName)
    {
        if (Clients.Count == 0)
        {
            return null;
        }

        if (contractType is not null)
        {
            var fullName = contractType.FullName ?? contractType.Name;

            // Nested contracts have a '+' in their full name; configuration written by hand
            // usually spells it with a '.'.
            if (Clients.TryGetValue(fullName, out var byType)
                || Clients.TryGetValue(fullName.Replace('+', '.'), out byType))
            {
                return byType;
            }
        }

        if (Clients.TryGetValue(clientName, out var byName))
        {
            return byName;
        }

        if (contractType is not null)
        {
            // The type is known, so a key only belongs to it when the key is a trailing part of
            // its full name: "Outer.ITicketAI" does, "Other.ITicketAI" is a different contract
            // that happens to share the name.
            var fullName = (contractType.FullName ?? contractType.Name).Replace('+', '.');

            foreach (var pair in Clients)
            {
                if (IsQualifiedFormOf(fullName, pair.Key.Replace('+', '.')))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        foreach (var pair in Clients)
        {
            if (IsQualifiedFormOf(pair.Key, clientName))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static bool IsQualifiedFormOf(string key, string name)
        => key.Length > name.Length
           && key.EndsWith(name, StringComparison.Ordinal)
           && key[key.Length - name.Length - 1] is '.' or '+';
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
