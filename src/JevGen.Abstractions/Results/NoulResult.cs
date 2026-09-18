namespace JevGen;

/// <summary>
/// The outcome of a Jev <c>noul</c> question: a probability that the proposition holds.
/// </summary>
/// <remarks>
/// A noul deliberately does not collapse to <see cref="bool"/>. Call <see cref="Value"/>
/// with an explicit threshold when a binary answer is required.
/// </remarks>
public readonly record struct NoulResult : IAIResult
{
    /// <summary>Creates a noul result.</summary>
    public NoulResult(double probability) => Probability = probability;

    /// <summary>The probability that the proposition is true, between 0 and 1.</summary>
    public double Probability { get; init; }

    /// <summary>Provenance for the evaluation that produced this result, when recorded.</summary>
    /// <remarks>
    /// Excluded from JSON. Results are frequently returned straight from an API, and provider
    /// names, model identifiers and request ids are internal operational detail rather than
    /// something to publish to callers. Read it in code; project it deliberately if you want it
    /// on the wire.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public EvaluationMetadata? Metadata { get; init; }

    /// <summary>
    /// Confidence in the binary reading of this result at the default 0.5 threshold:
    /// distance from maximum uncertainty, expressed in the same 0-1 range.
    /// </summary>
    public double Confidence => System.Math.Max(Probability, 1d - Probability);

    /// <summary>Interprets the probability as a boolean at the supplied threshold.</summary>
    /// <param name="threshold">The inclusive lower bound treated as true. Defaults to 0.5.</param>
    public bool Value(double threshold = 0.5) => Probability >= threshold;
}
