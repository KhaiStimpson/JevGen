using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JevGen.Testing;

/// <summary>Registers fake JevGen clients for tests.</summary>
public static class TestingServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the evaluation runtime with a scripted fake, so every registered JevGen client
    /// answers deterministically without a provider or a network.
    /// </summary>
    public static IServiceCollection AddJevFakeRuntime(
        this IServiceCollection services,
        Action<FakeEvaluationRuntime>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var runtime = new FakeEvaluationRuntime();
        configure?.Invoke(runtime);

        services.RemoveAll<IEvaluationRuntime>();
        services.AddSingleton(runtime);
        services.AddSingleton<IEvaluationRuntime>(runtime);

        // Contracts are never checked against a provider in a fake setup: there is no provider.
        services.Configure<JevGenOptions>(options => options.ValidateOnStart = false);

        return services;
    }

    /// <summary>Registers one contract backed by a scripted fake runtime.</summary>
    public static IServiceCollection AddJevFake<TContract>(
        this IServiceCollection services,
        Action<JevFakeClient<TContract>>? configure = null)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var fake = JevFake.Create<TContract>();
        configure?.Invoke(fake);

        services.RemoveAll<TContract>();
        services.AddSingleton(fake);
        services.AddSingleton(fake.Client);

        return services;
    }

    /// <summary>
    /// Wraps the registered runtime so live answers are recorded into a replayable fixture.
    /// </summary>
    public static IServiceCollection AddJevRecording(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IEvaluationRuntime))
            ?? throw new JevGenException(
                "No IEvaluationRuntime is registered. Call AddJevGen() before AddJevRecording().");

        services.Remove(existing);

        services.AddSingleton(provider =>
        {
            var inner = existing.ImplementationInstance as IEvaluationRuntime
                        ?? existing.ImplementationFactory?.Invoke(provider) as IEvaluationRuntime
                        ?? (IEvaluationRuntime)ActivatorUtilities.CreateInstance(
                            provider,
                            existing.ImplementationType
                            ?? throw new JevGenException("The registered IEvaluationRuntime cannot be wrapped."));

            return new RecordingEvaluationRuntime(inner);
        });

        services.AddSingleton<IEvaluationRuntime>(provider =>
            provider.GetRequiredService<RecordingEvaluationRuntime>());

        return services;
    }
}
