namespace JevGen;

/// <summary>Applies decision policies to model results.</summary>
public static class DecisionPolicyExtensions
{
    /// <summary>Classifies a choice result against a policy.</summary>
    public static Decision<T> Apply<T>(this ChoiceResult<T> result, DecisionPolicy<T> policy)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Apply(result.Value, result.Confidence, result.Metadata);
    }

    /// <summary>
    /// Classifies a noul result against a policy, reading the proposition at
    /// <paramref name="threshold"/> and using the distance from maximum uncertainty as the
    /// confidence.
    /// </summary>
    public static Decision<bool> Apply(
        this NoulResult result,
        DecisionPolicy<bool> policy,
        double threshold = 0.5d)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Apply(result.Value(threshold), result.Confidence, result.Metadata);
    }

    /// <summary>Classifies a score result against a policy.</summary>
    public static Decision<double> Apply(this ScoreResult result, DecisionPolicy<double> policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Apply(result.Value, result.Confidence, result.Metadata);
    }

    /// <summary>Reclassifies an already-classified decision against a different policy.</summary>
    public static Decision<T> Apply<T>(this Decision<T> decision, DecisionPolicy<T> policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Apply(decision.Value, decision.Confidence, decision.Metadata);
    }
}
