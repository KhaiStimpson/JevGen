namespace JevGen;

/// <summary>How agreeing provider decisions are combined into a consensus.</summary>
public enum ConsensusStrategy
{
    /// <summary>The value the most providers selected.</summary>
    Majority = 0,

    /// <summary>The value with the highest total confidence across providers.</summary>
    ConfidenceWeighted = 1,

    /// <summary>
    /// The first provider's value unless the others disagree strongly enough to overturn it.
    /// </summary>
    PrimaryWithVerification = 2,
}

/// <summary>
/// Combines several providers' decisions about the same state into one.
/// </summary>
/// <remarks>
/// Agreement is reported separately from confidence. Three providers that all answer with low
/// confidence agree completely and are still not trustworthy, and collapsing the two signals
/// would hide that.
/// </remarks>
public static class ConsensusAggregator
{
    /// <summary>Aggregates provider decisions.</summary>
    /// <param name="decisions">One decision per participating provider.</param>
    /// <param name="minimumAgreement">The share of providers that must agree, between 0 and 1.</param>
    /// <param name="strategy">How to pick the agreed value.</param>
    /// <exception cref="ArgumentException">No decisions were supplied.</exception>
    public static ConsensusResult<T> Aggregate<T>(
        IReadOnlyList<ProviderDecision<T>> decisions,
        double minimumAgreement = 0.5d,
        ConsensusStrategy strategy = ConsensusStrategy.Majority)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(decisions);

        if (decisions.Count == 0)
        {
            throw new ArgumentException("A consensus needs at least one provider decision.", nameof(decisions));
        }

        var winner = strategy switch
        {
            ConsensusStrategy.ConfidenceWeighted => ByTotalConfidence(decisions),
            ConsensusStrategy.PrimaryWithVerification => WithVerification(decisions, minimumAgreement),
            _ => ByCount(decisions),
        };

        var agreeing = decisions.Where(decision => EqualityComparer<T>.Default.Equals(decision.Value, winner)).ToList();
        var agreement = (double)agreeing.Count / decisions.Count;

        return new ConsensusResult<T>
        {
            Value = winner,
            Agreement = agreement,
            Decisions = decisions,
            Confidence = agreeing.Average(decision => decision.Confidence),
            HasConsensus = agreement >= minimumAgreement,
        };
    }

    private static T ByCount<T>(IReadOnlyList<ProviderDecision<T>> decisions)
        where T : notnull
        => decisions
            .GroupBy(decision => decision.Value)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Sum(decision => decision.Confidence))
            .First()
            .Key;

    private static T ByTotalConfidence<T>(IReadOnlyList<ProviderDecision<T>> decisions)
        where T : notnull
        => decisions
            .GroupBy(decision => decision.Value)
            .OrderByDescending(group => group.Sum(decision => decision.Confidence))
            .ThenByDescending(group => group.Count())
            .First()
            .Key;

    private static T WithVerification<T>(IReadOnlyList<ProviderDecision<T>> decisions, double minimumAgreement)
        where T : notnull
    {
        var primary = decisions[0].Value;

        var supporting = decisions.Count(decision => EqualityComparer<T>.Default.Equals(decision.Value, primary));

        // The primary stands unless the verifiers fail to reach the agreement bar behind it.
        return (double)supporting / decisions.Count >= minimumAgreement ? primary : ByCount(decisions);
    }
}
