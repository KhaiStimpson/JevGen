namespace JevGen;

/// <summary>
/// Common shape of a model decision that carries a confidence signal.
/// </summary>
public interface IAIResult
{
    /// <summary>Model confidence in the decision, between 0 and 1.</summary>
    double Confidence { get; }
}
