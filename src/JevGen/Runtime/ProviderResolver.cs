using JevGen.Providers;

namespace JevGen;

/// <summary>
/// The default <see cref="IJevProviderResolver"/>: an ordered, name-indexed view over the
/// registered providers.
/// </summary>
public sealed class ProviderResolver : IJevProviderResolver
{
    private readonly Dictionary<string, IJevProvider> _byName;
    private readonly string? _defaultName;

    /// <summary>Creates a resolver over a set of providers.</summary>
    /// <param name="providers">The registered providers, in registration order.</param>
    /// <param name="defaultProviderName">
    /// The configured default provider name. When null the first registered provider is the default.
    /// </param>
    public ProviderResolver(IEnumerable<IJevProvider> providers, string? defaultProviderName = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        All = [.. providers];
        _byName = new Dictionary<string, IJevProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in All)
        {
            // Later registrations of the same name replace earlier ones, matching DI semantics.
            _byName[provider.Name] = provider;
        }

        _defaultName = defaultProviderName;
    }

    /// <inheritdoc />
    public IReadOnlyList<IJevProvider> All { get; }

    /// <inheritdoc />
    public IJevProvider? Default
    {
        get
        {
            if (_defaultName is not null)
            {
                return _byName.TryGetValue(_defaultName, out var named)
                    ? named
                    : throw new JevGenException(
                        $"The configured default provider '{_defaultName}' is not registered. " +
                        $"Registered providers: {DescribeRegistered()}.");
            }

            return All.Count > 0 ? All[0] : null;
        }
    }

    /// <inheritdoc />
    public bool TryResolve(string name, out IJevProvider provider)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out provider!);
    }

    /// <inheritdoc />
    public IJevProvider Resolve(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_byName.TryGetValue(name, out var provider))
        {
            throw new JevGenException(
                $"No JevGen provider named '{name}' is registered. " +
                $"Registered providers: {DescribeRegistered()}.");
        }

        return provider;
    }

    private string DescribeRegistered()
        => All.Count == 0 ? "(none)" : string.Join(", ", All.Select(p => $"'{p.Name}'"));
}
