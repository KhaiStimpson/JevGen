namespace JevGen.Providers;

/// <summary>
/// Resolves providers by name. Registration order establishes the default provider.
/// </summary>
public interface IJevProviderResolver
{
    /// <summary>
    /// The provider used when nothing overrides the selection, or <see langword="null"/>
    /// when no provider is registered.
    /// </summary>
    IJevProvider? Default { get; }

    /// <summary>Every registered provider, in registration order.</summary>
    IReadOnlyList<IJevProvider> All { get; }

    /// <summary>Resolves a provider by its registered name.</summary>
    /// <returns><see langword="true"/> when a provider with that name is registered.</returns>
    bool TryResolve(string name, out IJevProvider provider);

    /// <summary>Resolves a provider by name, throwing when it is not registered.</summary>
    /// <exception cref="JevGenException">No provider is registered under that name.</exception>
    IJevProvider Resolve(string name);
}
