namespace JevGen;

/// <summary>
/// The outcome of a Jev <c>score</c> question.
/// </summary>
public readonly record struct ScoreResult : IAIResult
{
    /// <summary>Creates a score result.</summary>
    public ScoreResult(double value, double? confidence = null)
    {
        Value = value;
        ConfidenceOrNull = confidence;
    }

    /// <summary>The score the model produced, on the scale declared by the question.</summary>
    public double Value { get; init; }

    /// <summary>The model's confidence in the score, when the provider reports one.</summary>
    public double? ConfidenceOrNull { get; init; }

    /// <summary>Provenance for the evaluation that produced this result, when recorded.</summary>
    /// <remarks>
    /// Excluded from JSON. Results are frequently returned straight from an API, and provider
    /// names, model identifiers and request ids are internal operational detail rather than
    /// something to publish to callers. Read it in code; project it deliberately if you want it
    /// on the wire.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public EvaluationMetadata? Metadata { get; init; }

    /// <summary>Confidence in the score, or 1 when the provider does not report one.</summary>
    public double Confidence => ConfidenceOrNull ?? 1d;

    /// <summary>Rescales the score from the supplied source range onto <c>[0, 1]</c>.</summary>
    public double Normalize(double min, double max)
        => max <= min ? 0d : System.Math.Clamp((Value - min) / (max - min), 0d, 1d);
}
