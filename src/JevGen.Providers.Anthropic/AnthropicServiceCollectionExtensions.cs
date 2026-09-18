using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.Anthropic;

/// <summary>Registers the Anthropic evaluation provider.</summary>
public static class AnthropicServiceCollectionExtensions
{
    /// <summary>Adds the Anthropic evaluation provider.</summary>
    public static IServiceCollection AddAnthropicEvaluation(
        this IServiceCollection services,
        Action<AnthropicJevOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<AnthropicJevOptions>(AnthropicJevProvider.ProviderName).Configure(configure);

        services.AddHttpClient<AnthropicJevProvider>()
            .ConfigureHttpClient(static (provider, client) =>
                client.Timeout = provider
                    .GetRequiredService<IOptionsMonitor<AnthropicJevOptions>>()
                    .Get(AnthropicJevProvider.ProviderName)
                    .Timeout);

        services.AddSingleton<IJevProvider>(static provider =>
            provider.GetRequiredService<AnthropicJevProvider>());

        return services;
    }

    /// <summary>Adds the Anthropic evaluation provider, bound from configuration.</summary>
    public static IServiceCollection AddAnthropicEvaluation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddAnthropicEvaluation(options => configuration.Bind(options));
    }

    /// <summary>Runs this contract on Anthropic.</summary>
    public static JevClientBuilder<TContract> UseAnthropic<TContract>(this JevClientBuilder<TContract> builder)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseProvider(AnthropicJevProvider.ProviderName);
    }
}
