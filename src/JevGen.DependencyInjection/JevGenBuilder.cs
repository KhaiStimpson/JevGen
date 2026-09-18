using Microsoft.Extensions.DependencyInjection;

namespace JevGen;

/// <summary>The root builder returned by <c>AddJevGen()</c>.</summary>
public sealed class JevGenBuilder
{
    internal JevGenBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// The builder returned by <c>AddJevClient&lt;T&gt;()</c>, for configuring one contract.
/// </summary>
/// <typeparam name="TContract">The contract interface.</typeparam>
public sealed class JevClientBuilder<TContract>
    where TContract : class
{
    internal JevClientBuilder(IServiceCollection services) => Services = services;

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Applies configuration to this contract.</summary>
    public JevClientBuilder<TContract> Configure(Action<JevClientConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Services.Configure<JevGenOptions>(options => configure(options.ClientFor(typeof(TContract))));
        return this;
    }

    /// <summary>Routes this contract to a registered provider by name.</summary>
    public JevClientBuilder<TContract> UseProvider(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Configure(configuration => configuration.Provider = name);
    }

    /// <summary>Routes this contract to a registered provider by type.</summary>
    public JevClientBuilder<TContract> UseProvider<TProvider>()
        where TProvider : class, Providers.IJevProvider
        => UseProvider(JevProviderNames.Of<TProvider>());

    /// <summary>Overrides the model this contract uses.</summary>
    public JevClientBuilder<TContract> UseModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        return Configure(configuration => configuration.Model = model);
    }

    /// <summary>
    /// Adds a provider to try when the primary provider cannot serve the request.
    /// </summary>
    /// <remarks>
    /// Fallback triggers on provider unavailability, rate limiting, timeouts, unsupported
    /// capabilities and failures explicitly classified as transient. It never triggers on
    /// authentication failures or malformed contracts, which are deterministic and would fail
    /// identically everywhere.
    /// </remarks>
    public JevClientBuilder<TContract> FallbackTo(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Configure(configuration => configuration.FallbackProviders.Add(name));
    }

    /// <summary>Adds a provider to try when the primary provider cannot serve the request.</summary>
    public JevClientBuilder<TContract> FallbackTo<TProvider>()
        where TProvider : class, Providers.IJevProvider
        => FallbackTo(JevProviderNames.Of<TProvider>());

    /// <summary>
    /// Falls back to the next provider when the primary answers below this confidence.
    /// The highest-confidence answer is returned once the chain is exhausted.
    /// </summary>
    public JevClientBuilder<TContract> FallbackWhenConfidenceBelow(double threshold)
    {
        if (threshold is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold), threshold, "A confidence threshold must be between 0 and 1.");
        }

        return Configure(configuration => configuration.FallbackWhenConfidenceBelow = threshold);
    }

    /// <summary>
    /// Supplies provider-specific options for this contract. They only ever reach the named
    /// provider, so a contract configured for one host stays correct on every other.
    /// </summary>
    public JevClientBuilder<TContract> ConfigureProvider(
        string provider,
        Action<IDictionary<string, object?>> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(configure);

        return Configure(configuration => configure(configuration.ProviderOptionsFor(provider)));
    }

    /// <summary>
    /// Opts this contract into degraded behaviour when the provider lacks a required capability.
    /// </summary>
    /// <remarks>
    /// JevGen fails on a capability mismatch by default. This is the explicit opt-in the design
    /// requires before an application accepts weaker semantics than its contract asks for.
    /// </remarks>
    public JevClientBuilder<TContract> AllowDegradedCapabilities()
        => Configure(configuration => configuration.CapabilityValidation = CapabilityValidationMode.Warn);
}
