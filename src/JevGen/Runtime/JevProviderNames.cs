using JevGen.Providers;

namespace JevGen;

/// <summary>
/// Resolves a provider type to its registered name.
/// </summary>
/// <remarks>
/// <para>
/// Provider names come from <see cref="IJevProvider.Name"/>, which is an instance member. A
/// registration-time API such as <c>UseProvider&lt;T&gt;()</c> has no instance to ask, so the
/// type is recorded and resolved when the container is available.
/// </para>
/// <para>
/// Reading the name reflectively would be simpler and wrong: it would not survive trimming, and
/// it would disagree with the provider's own <see cref="IJevProvider.Name"/> whenever a provider
/// computes it.
/// </para>
/// </remarks>
public static class JevProviderNames
{
    /// <summary>Finds the registered name of <paramref name="providerType"/> among registered providers.</summary>
    /// <exception cref="JevGenException">No provider of that type is registered.</exception>
    public static string Resolve(Type providerType, IJevProviderResolver providers)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        ArgumentNullException.ThrowIfNull(providers);

        foreach (var provider in providers.All)
        {
            if (Matches(provider, providerType))
            {
                return provider.Name;
            }
        }

        throw new JevGenException(
            $"No JevGen provider of type '{providerType}' is registered. Register it with " +
            $"AddJevProvider<{providerType.Name}>() before selecting it.");
    }

    /// <summary>Finds a registered provider by its implementation type.</summary>
    public static bool TryResolve(Type providerType, IJevProviderResolver providers, out IJevProvider provider)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        ArgumentNullException.ThrowIfNull(providers);

        foreach (var candidate in providers.All)
        {
            if (Matches(candidate, providerType))
            {
                provider = candidate;
                return true;
            }
        }

        provider = null!;
        return false;
    }

    /// <summary>
    /// Whether a registered provider is of the requested type, seeing through the wrapper used
    /// for named registration.
    /// </summary>
    private static bool Matches(IJevProvider provider, Type providerType)
        => providerType.IsInstanceOfType(provider)
           || (provider is NamedJevProvider named && providerType.IsInstanceOfType(named.Inner));
}
