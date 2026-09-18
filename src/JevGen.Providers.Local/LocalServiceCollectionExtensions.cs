using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.Local;

/// <summary>Registers a self-hosted Jev provider.</summary>
public static class LocalServiceCollectionExtensions
{
    /// <summary>Adds a self-hosted Jev provider.</summary>
    public static IServiceCollection AddLocalJev(
        this IServiceCollection services,
        Action<LocalJevOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<LocalJevOptions>(LocalJevProvider.ProviderName).Configure(configure);

        services.AddHttpClient<LocalJevProvider>()
            .ConfigureHttpClient(static (provider, client) =>
            {
                client.Timeout = provider
                    .GetRequiredService<IOptionsMonitor<LocalJevOptions>>()
                    .Get(LocalJevProvider.ProviderName)
                    .Timeout;
            });

        services.AddSingleton<IJevProvider>(static provider =>
            provider.GetRequiredService<LocalJevProvider>());

        return services;
    }

    /// <summary>Adds a self-hosted Jev provider, bound from configuration.</summary>
    /// <remarks>
    /// Configuration binding walks the options type reflectively. Trimmed and Native AOT
    /// applications should use the delegate overload, or the configuration binding source
    /// generator.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Binding provider options from configuration uses reflection over their members. Use the delegate overload in a trimmed application.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "Binding provider options from configuration may require dynamic code. Use the delegate overload in a Native AOT application.")]
    public static IServiceCollection AddLocalJev(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddLocalJev(options => configuration.Bind(options));
    }

    /// <summary>Runs this contract against the self-hosted Jev endpoint.</summary>
    public static JevClientBuilder<TContract> UseLocal<TContract>(this JevClientBuilder<TContract> builder)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseProvider(LocalJevProvider.ProviderName);
    }
}
