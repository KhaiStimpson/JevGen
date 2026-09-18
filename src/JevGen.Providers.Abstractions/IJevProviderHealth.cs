namespace JevGen.Providers;

/// <summary>
/// An optional capability a provider can implement so that health checks can probe it
/// without issuing a billable evaluation.
/// </summary>
public interface IJevProviderHealth
{
    /// <summary>Checks that the provider is configured and reachable.</summary>
    ValueTask<JevProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a provider health probe.</summary>
/// <param name="IsHealthy">Whether the provider is usable.</param>
/// <param name="Description">A human-readable explanation, shown in health-check output.</param>
/// <param name="Exception">The failure that made the provider unhealthy, when there was one.</param>
public readonly record struct JevProviderHealthResult(
    bool IsHealthy,
    string? Description = null,
    Exception? Exception = null)
{
    /// <summary>A healthy result.</summary>
    public static JevProviderHealthResult Healthy(string? description = null) => new(true, description);

    /// <summary>An unhealthy result.</summary>
    public static JevProviderHealthResult Unhealthy(string description, Exception? exception = null)
        => new(false, description, exception);
}
