namespace JevGen;

/// <summary>How an application should treat a model decision at its confidence level.</summary>
/// <remarks>
/// A policy classifies the result. It never executes business side effects: acting on the
/// classification remains the application's responsibility.
/// </remarks>
public enum DecisionAction
{
    /// <summary>Confidence is high enough to act on automatically.</summary>
    Accept = 0,

    /// <summary>Confidence warrants human review before acting.</summary>
    Review = 1,

    /// <summary>Confidence is too low to use.</summary>
    Reject = 2,
}
