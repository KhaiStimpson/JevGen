namespace JevGen;

/// <summary>A model decision that has been classified by a decision policy.</summary>
/// <typeparam name="T">The decided value type.</typeparam>
public readonly record struct Decision<T> : IAIResult
{
    /// <summary>Creates a decision.</summary>
    public Decision(T value, double confidence, DecisionAction action)
    {
        Value = value;
        Confidence = confidence;
        Action = action;
    }

    /// <summary>The value the model decided on.</summary>
    public T Value { get; init; }

    /// <summary>The model's confidence in <see cref="Value"/>.</summary>
    public double Confidence { get; init; }

    /// <summary>How the policy classified this confidence level.</summary>
    public DecisionAction Action { get; init; }

    /// <summary>Provenance for the evaluation that produced this decision, when recorded.</summary>
    public EvaluationMetadata? Metadata { get; init; }

    /// <summary>Whether the policy classified the decision as <see cref="DecisionAction.Accept"/>.</summary>
    public bool IsAccepted => Action == DecisionAction.Accept;

    /// <summary>Whether the policy classified the decision as <see cref="DecisionAction.Review"/>.</summary>
    public bool NeedsReview => Action == DecisionAction.Review;

    /// <summary>Whether the policy classified the decision as <see cref="DecisionAction.Reject"/>.</summary>
    public bool IsRejected => Action == DecisionAction.Reject;
}
