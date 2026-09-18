using JevGen.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace JevGen.AspNetCore;

/// <summary>
/// Reports whether JevGen is configured and its providers can serve the registered contracts.
/// </summary>
/// <remarks>
/// The check never issues an evaluation. Health probes run constantly, and an evaluation is
/// billable, so the check verifies configuration and capability compatibility, and asks each
/// provider for its own cheap readiness signal where it offers one.
/// </remarks>
public sealed class JevHealthCheck(
    IJevProviderResolver providers,
    IOptionsMonitor<JevGenOptions> options) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["providers"] = providers.All.Select(provider => provider.Name).ToArray(),
            ["contracts"] = JevClientRegistry.All.Select(descriptor => descriptor.Name).ToArray(),
        };

        if (providers.All.Count == 0)
        {
            return HealthCheckResult.Unhealthy("No JevGen provider is registered.", data: data);
        }

        var unhealthy = new List<string>();

        foreach (var provider in providers.All)
        {
            if (provider is not IJevProviderHealth health)
            {
                continue;
            }

            var result = await health.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

            if (!result.IsHealthy)
            {
                unhealthy.Add($"{provider.Name}: {result.Description ?? "unhealthy"}");
            }
        }

        if (unhealthy.Count > 0)
        {
            data["unhealthyProviders"] = unhealthy.ToArray();
            return HealthCheckResult.Unhealthy(
                "One or more JevGen providers are not usable: " + string.Join("; ", unhealthy),
                data: data);
        }

        var violations = CapabilityValidator.Validate(JevClientRegistry.All, providers, options.CurrentValue);

        if (violations.Count > 0)
        {
            data["capabilityViolations"] = violations.Select(violation => violation.Contract).ToArray();

            return HealthCheckResult.Degraded(
                "Some JevGen contracts require semantics their provider does not support.",
                data: data);
        }

        return HealthCheckResult.Healthy("JevGen is configured and every contract is satisfiable.", data);
    }
}

/// <summary>Registers the JevGen health check.</summary>
public static class JevHealthCheckBuilderExtensions
{
    /// <summary>Adds a health check for JevGen's providers and contracts.</summary>
    public static IHealthChecksBuilder AddJev(
        this IHealthChecksBuilder builder,
        string name = "jevgen",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<JevHealthCheck>(name, failureStatus, tags ?? []);
    }
}
