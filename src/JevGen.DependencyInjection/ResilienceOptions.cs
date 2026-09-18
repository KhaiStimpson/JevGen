namespace JevGen;

/// <summary>
/// Retry, timeout and circuit-breaker settings for evaluations.
/// </summary>
/// <remarks>
/// Defaults are deliberately conservative. An evaluation is usually billable and often on a
/// user-facing path, so retrying aggressively costs money and latency without improving the
/// answer.
/// </remarks>
public sealed class JevResilienceOptions
{
    /// <summary>The time budget for one provider attempt.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How many times to retry a transient failure. Two by default.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>The delay before the first retry; later retries back off exponentially.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The longest delay between retries, whatever the backoff computes.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether to add random jitter to retry delays, so concurrent callers do not retry in
    /// lockstep after a shared outage.
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Consecutive failures that open the circuit for a provider. Zero disables the breaker.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>How long the circuit stays open before a probe is allowed through.</summary>
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Decides whether a failure may be retried. Replacing it overrides the built-in
    /// classification entirely.
    /// </summary>
    /// <remarks>
    /// Authentication failures, malformed contracts and rejected requests are never retried by
    /// the default classifier: they fail identically on every attempt, so retrying only spends
    /// the caller's latency budget.
    /// </remarks>
    public Func<Exception, bool> ShouldRetry { get; set; } = IsTransient;

    /// <summary>The built-in transient-failure classification.</summary>
    public static bool IsTransient(Exception exception) => exception switch
    {
        EvaluationAuthenticationException => false,
        EvaluationResponseException => false,
        EvaluationSerializationException => false,
        EvaluationCapabilityException => false,
        EvaluationRateLimitException => true,
        EvaluationTimeoutException => true,
        EvaluationProviderException provider => provider.IsTransient,
        HttpRequestException => true,
        TimeoutException => true,
        _ => false,
    };
}
