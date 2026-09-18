using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JevGen.Providers.TypeSafe;

/// <summary>Registers the TypeSafe Jev provider.</summary>
public static class TypeSafeServiceCollectionExtensions
{
    /// <summary>Adds the TypeSafe Jev provider.</summary>
    public static IServiceCollection AddTypeSafeJev(
        this IServiceCollection services,
        Action<TypeSafeJevOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<TypeSafeJevOptions>(TypeSafeJevProvider.ProviderName)
            .Configure(configure);

        services.AddHttpClient<TypeSafeJevProvider>()
            .ConfigureHttpClient(static (provider, client) =>
            {
                var options = provider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<TypeSafeJevOptions>>()
                    .Get(TypeSafeJevProvider.ProviderName);

                client.Timeout = options.Timeout;
            });

        services.AddSingleton<IJevProvider>(static provider =>
            provider.GetRequiredService<TypeSafeJevProvider>());

        return services;
    }

    /// <summary>Adds the TypeSafe Jev provider, bound from configuration.</summary>
    /// <remarks>
    /// Configuration binding walks the options type reflectively. Trimmed and Native AOT
    /// applications should use the delegate overload, or the configuration binding source
    /// generator.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Binding provider options from configuration uses reflection over their members. Use the delegate overload in a trimmed application.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "Binding provider options from configuration may require dynamic code. Use the delegate overload in a Native AOT application.")]
    public static IServiceCollection AddTypeSafeJev(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddTypeSafeJev(options => configuration.Bind(options));
    }
}

/// <summary>Routes a contract to the TypeSafe Jev provider.</summary>
public static class TypeSafeClientBuilderExtensions
{
    /// <summary>Runs this contract against the TypeSafe Jev API directly.</summary>
    public static JevClientBuilder<TContract> UseTypeSafe<TContract>(this JevClientBuilder<TContract> builder)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseProvider(TypeSafeJevProvider.ProviderName);
    }

    /// <summary>Falls back to the TypeSafe Jev API when the primary provider cannot serve a request.</summary>
    public static JevClientBuilder<TContract> FallbackToTypeSafe<TContract>(this JevClientBuilder<TContract> builder)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.FallbackTo(TypeSafeJevProvider.ProviderName);
    }
}
