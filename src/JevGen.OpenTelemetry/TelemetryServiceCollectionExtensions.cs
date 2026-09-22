using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JevGen.Telemetry;

/// <summary>Registers JevGen's OpenTelemetry instrumentation.</summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>Adds JevGen instrumentation with default, conservative recording settings.</summary>
    public static JevGenBuilder AddJevGenTelemetry(this IServiceCollection services)
        => services.AddJevGenTelemetry(static _ => { });

    /// <summary>Adds JevGen instrumentation and configures what it records.</summary>
    public static JevGenBuilder AddJevGenTelemetry(
        this IServiceCollection services,
        Action<JevGenTelemetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = services.AddJevGen();
        services.AddOptions<JevGenTelemetryOptions>().Configure(configure);

        // Registered once however often this is called, so spans and metrics are never doubled.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEvaluationFilter, TelemetryEvaluationFilter>());

        return builder;
    }

    /// <summary>Adds JevGen instrumentation and configures what it records.</summary>
    public static JevGenBuilder AddJevGenTelemetry(
        this JevGenBuilder builder,
        Action<JevGenTelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Services.AddJevGenTelemetry(configure ?? (static _ => { }));
    }
}
