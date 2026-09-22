using Microsoft.Extensions.DependencyInjection;

namespace JevGen;

/// <summary>Adds resilience to JevGen evaluations.</summary>
public static class JevResilienceExtensions
{
    /// <summary>Adds retry, per-attempt timeout and a per-provider circuit breaker.</summary>
    /// <remarks>
    /// This applies to every provider uniformly, including custom ones that do not use an
    /// <see cref="HttpClient"/>. Applications that prefer to keep resilience at the transport
    /// can instead configure <c>Microsoft.Extensions.Http.Resilience</c> on each provider's
    /// HTTP client, and leave this off.
    /// </remarks>
    public static JevGenBuilder AddResilience(
        this JevGenBuilder builder,
        Action<JevResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<JevResilienceOptions>().Configure(configure ?? (static _ => { }));

        // Registered once however often this is called: a second filter would nest a second
        // retry loop inside the first and multiply the attempts.
        builder.Services.TryAddEnumerableFilter();

        return builder;
    }

    /// <summary>Adds resilience from one contract's builder chain.</summary>
    /// <remarks>
    /// Resilience settings are global: this is a convenience for fluent registration, and the
    /// settings it configures apply to every contract, not only <typeparamref name="TContract"/>.
    /// </remarks>
    public static JevClientBuilder<TContract> AddResilience<TContract>(
        this JevClientBuilder<TContract> builder,
        Action<JevResilienceOptions>? configure = null)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<JevResilienceOptions>().Configure(configure ?? (static _ => { }));
        builder.Services.TryAddEnumerableFilter();

        return builder;
    }

    private static void TryAddEnumerableFilter(this IServiceCollection services)
        => Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
            .TryAddEnumerable(
                services,
                ServiceDescriptor.Singleton<IEvaluationFilter, ResilienceEvaluationFilter>());
}
