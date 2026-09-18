using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.Gemini;

/// <summary>Registers the Gemini evaluation provider.</summary>
public static class GeminiServiceCollectionExtensions
{
    /// <summary>Adds the Gemini evaluation provider.</summary>
    public static IServiceCollection AddGeminiEvaluation(
        this IServiceCollection services,
        Action<GeminiJevOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<GeminiJevOptions>(GeminiJevProvider.ProviderName).Configure(configure);

        services.AddHttpClient<GeminiJevProvider>()
            .ConfigureHttpClient(static (provider, client) =>
                client.Timeout = provider
                    .GetRequiredService<IOptionsMonitor<GeminiJevOptions>>()
                    .Get(GeminiJevProvider.ProviderName)
                    .Timeout);

        services.AddSingleton<IJevProvider>(static provider =>
            provider.GetRequiredService<GeminiJevProvider>());

        return services;
    }

    /// <summary>Adds the Gemini evaluation provider, bound from configuration.</summary>
    public static IServiceCollection AddGeminiEvaluation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddGeminiEvaluation(options => configuration.Bind(options));
    }

    /// <summary>Runs this contract on Gemini.</summary>
    public static JevClientBuilder<TContract> UseGemini<TContract>(this JevClientBuilder<TContract> builder)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseProvider(GeminiJevProvider.ProviderName);
    }
}
