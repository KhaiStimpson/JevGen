using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.OpenRouter;

/// <summary>Registers the OpenRouter Jev provider.</summary>
public static class OpenRouterServiceCollectionExtensions
{
    /// <summary>Adds the OpenRouter Jev provider.</summary>
    public static IServiceCollection AddOpenRouterJev(
        this IServiceCollection services,
        Action<OpenRouterJevOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<OpenRouterJevOptions>(OpenRouterJevProvider.ProviderName).Configure(configure);

        services.AddHttpClient<OpenRouterJevProvider>()
            .ConfigureHttpClient(static (provider, client) =>
            {
                client.Timeout = provider
                    .GetRequiredService<IOptionsMonitor<OpenRouterJevOptions>>()
                    .Get(OpenRouterJevProvider.ProviderName)
                    .Timeout;
            });

        services.AddSingleton<IJevProvider>(static provider =>
            provider.GetRequiredService<OpenRouterJevProvider>());

        return services;
    }

    /// <summary>Adds the OpenRouter Jev provider, bound from configuration.</summary>
    /// <remarks>
    /// Configuration binding walks the options type reflectively. Trimmed and Native AOT
    /// applications should use the delegate overload, or the configuration binding source
    /// generator.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Binding provider options from configuration uses reflection over their members. Use the delegate overload in a trimmed application.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "Binding provider options from configuration may require dynamic code. Use the delegate overload in a Native AOT application.")]
    public static IServiceCollection AddOpenRouterJev(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddOpenRouterJev(options => configuration.Bind(options));
    }
}

/// <summary>Routes a contract to Jev hosted through OpenRouter.</summary>
public static class OpenRouterClientBuilderExtensions
{
    /// <summary>Runs this contract against Jev hosted through OpenRouter.</summary>
    public static JevClientBuilder<TContract> UseOpenRouter<TContract>(this JevClientBuilder<TContract> builder)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseProvider(OpenRouterJevProvider.ProviderName);
    }

    /// <summary>Falls back to OpenRouter when the primary provider cannot serve a request.</summary>
    public static JevClientBuilder<TContract> FallbackToOpenRouter<TContract>(this JevClientBuilder<TContract> builder)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.FallbackTo(OpenRouterJevProvider.ProviderName);
    }
}
