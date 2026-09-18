using JevGen.Providers;

namespace JevGen;

/// <summary>
/// The mutable state of one evaluation as it moves through the filter pipeline.
/// </summary>
public sealed class EvaluationContext
{
    internal EvaluationContext(EvaluationRequest request, IJevProvider provider, int attempt)
    {
        Request = request;
        Provider = provider;
        Attempt = attempt;
    }

    /// <summary>The canonical request.</summary>
    public EvaluationRequest Request { get; internal set; }

    /// <summary>The provider selected for this attempt.</summary>
    public IJevProvider Provider { get; internal set; }

    /// <summary>The one-based attempt number, counting retries and fallbacks.</summary>
    public int Attempt { get; internal set; }

    /// <summary>Scratch space shared between filters for the life of the evaluation.</summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}

/// <summary>The continuation passed to an <see cref="IEvaluationFilter"/>.</summary>
public delegate ValueTask<EvaluationResponse> EvaluationDelegate(
    EvaluationContext context,
    CancellationToken cancellationToken);

/// <summary>
/// A cross-cutting concern that wraps provider calls.
/// </summary>
/// <remarks>
/// Filters are how telemetry, resilience, auditing and policy hook into evaluation without the
/// runtime taking a dependency on any of them, and without generated clients changing.
/// Lower <see cref="Order"/> values run further out.
/// </remarks>
public interface IEvaluationFilter
{
    /// <summary>Relative position in the pipeline. Lower values run further out.</summary>
    int Order => 0;

    /// <summary>Wraps the rest of the pipeline.</summary>
    ValueTask<EvaluationResponse> InvokeAsync(
        EvaluationContext context,
        EvaluationDelegate next,
        CancellationToken cancellationToken = default);
}
