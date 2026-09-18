namespace JevGen;

/// <summary>
/// The single abstraction generated clients depend on.
/// </summary>
/// <remarks>
/// The runtime resolves a provider, validates contract capabilities against it, runs the
/// configured filter pipeline (telemetry, resilience, policy) and orchestrates fallback.
/// Generated code never constructs HTTP requests or touches provider types directly, so
/// changing hosting never changes application contracts.
/// </remarks>
public interface IEvaluationRuntime
{
    /// <summary>Evaluates a request and returns the canonical response.</summary>
    ValueTask<EvaluationResponse> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken cancellationToken = default);
}
