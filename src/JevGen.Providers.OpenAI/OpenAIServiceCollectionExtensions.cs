using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JevGen.Providers.OpenAI;

/// <summary>Registers the OpenAI evaluation provider.</summary>
public static class OpenAIServiceCollectionExtensions
{
    /// <summary>Adds the OpenAI evaluation provider.</summary>
    public static IServiceCollection AddOpenAIEvaluation(
        this IServiceCollection services,
        Action<OpenAIJevOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<OpenAIJevOptions>(OpenAIJevProvider.ProviderName).Configure(configure);

        services.AddHttpClient<OpenAIJevProvider>()
            .ConfigureHttpClient(static (provider, client) =>
                client.Timeout = provider
                    .GetRequiredService<IOptionsMonitor<OpenAIJevOptions>>()
                    .Get(OpenAIJevProvider.ProviderName)
                    .Timeout);

        services.AddSingleton<IJevProvider>(static provider =>
            provider.GetRequiredService<OpenAIJevProvider>());

        return services;
    }

    /// <summary>Adds the OpenAI evaluation provider, bound from configuration.</summary>
    public static IServiceCollection AddOpenAIEvaluation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddOpenAIEvaluation(options => configuration.Bind(options));
    }

    /// <summary>Runs this contract on OpenAI.</summary>
    public static JevClientBuilder<TContract> UseOpenAI<TContract>(this JevClientBuilder<TContract> builder)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseProvider(OpenAIJevProvider.ProviderName);
    }
}
