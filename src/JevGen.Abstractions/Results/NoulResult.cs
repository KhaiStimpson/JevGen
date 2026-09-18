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
