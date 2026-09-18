using JevGen.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace JevGen;

/// <summary>
/// Registers evaluation providers.
/// </summary>
/// <remarks>
/// Any type implementing <see cref="IJevProvider"/> can be registered here, whether it ships
/// with JevGen, comes from a third-party package or lives in the application. A custom
/// provider needs no generator changes, no new attributes and no change to the contracts that
/// will run on it.
/// </remarks>
public static class JevProviderServiceCollectionExtensions
{
    /// <summary>Registers a provider resolved from the container.</summary>
    public static IServiceCollection AddJevProvider<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
        this IServiceCollection services)
        where TProvider : class, IJevProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TProvider>();
        services.AddSingleton<IJevProvider>(provider => provider.GetRequiredService<TProvider>());
        return services;
    }

    /// <summary>Registers a provider and configures its options.</summary>
    public static IServiceCollection AddJevProvider<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider,
        TOptions>(
        this IServiceCollection services,
        Action<TOptions> configure)
        where TProvider : class, IJevProvider
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<TOptions>().Configure(configure);
        return services.AddJevProvider<TProvider>();
    }

    /// <summary>Registers a provider under an explicit name, built by a factory.</summary>
    public static IServiceCollection AddJevProvider(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, IJevProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        services.AddSingleton<IJevProvider>(provider => new NamedJevProvider(name, factory(provider)));
        return services;
    }

    /// <summary>Registers an existing provider instance under an explicit name.</summary>
    public static IServiceCollection AddJevProvider(
        this IServiceCollection services,
        string name,
        IJevProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return services.AddJevProvider(name, _ => provider);
    }

    /// <summary>Registers a provider type under an explicit name.</summary>
    public static IServiceCollection AddJevProvider<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
        this IServiceCollection services,
        string name)
        where TProvider : class, IJevProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TProvider>();
        return services.AddJevProvider(name, provider => provider.GetRequiredService<TProvider>());
    }

    /// <summary>Makes a registered provider the default for contracts that do not choose one.</summary>
    public static IServiceCollection UseDefaultJevProvider(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.Configure<JevGenOptions>(options => options.DefaultProvider = name);
        return services;
    }
}
