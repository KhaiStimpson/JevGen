using Xunit;

namespace JevGen.Runtime.Tests;

/// <summary>Covers decision policies and consensus aggregation.</summary>
public sealed class PolicyTests
{
    private static ChoiceResult<Department> Choice(double confidence)
        => ChoiceResult.From(Department.Billing, confidence);

    [Theory]
    [InlineData(0.95, DecisionAction.Accept)]
    [InlineData(0.90, DecisionAction.Accept)]
    [InlineData(0.80, DecisionAction.Review)]
    [InlineData(0.65, DecisionAction.Review)]
    [InlineData(0.40, DecisionAction.Reject)]
    public void ThresholdsClassifyOnTheDocumentedBoundaries(double confidence, DecisionAction expected)
    {
        var policy = DecisionPolicy<Department>
            .AcceptAbove(0.90)
            .ReviewBetween(0.65, 0.90);

        Assert.Equal(expected, Choice(confidence).Apply(policy).Action);
    }

    [Fact]
    public void ThePolicyClassifiesWithoutChangingTheValue()
    {
        var result = new ChoiceResult<Department>(
            Department.Technical,
            0.5,
            new Dictionary<Department, double> { [Department.Technical] = 0.5, [Department.Billing] = 0.5 });

        var decision = result.Apply(DecisionPolicy<Department>.Default);

        // A rejected decision still carries what the model actually said: the policy is a
        // classification, not a veto that discards the answer.
        Assert.Equal(DecisionAction.Reject, decision.Action);
        Assert.Equal(Department.Technical, decision.Value);
        Assert.Equal(0.5, decision.Confidence, 6);
    }

    [Fact]
    public void RejectBelowAndAcceptAboveComposeIntoAReviewBand()
    {
        var policy = DecisionPolicy<Department>.AcceptAbove(0.85).RejectBelow(0.5);

        Assert.Equal(DecisionAction.Accept, policy.Classify(0.9));
        Assert.Equal(DecisionAction.Review, policy.Classify(0.6));
        Assert.Equal(DecisionAction.Reject, policy.Classify(0.4));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void ThresholdsOutsideZeroToOneAreRejected(double threshold)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DecisionPolicy<Department>.AcceptAbove(threshold));

    [Fact]
    public void NoulPolicyUsesDistanceFromUncertaintyAsConfidence()
    {
        // A 0.05 probability is a confident "no", not an unconfident answer.
        var decision = new NoulResult(0.05).Apply(DecisionPolicy<bool>.Default);

        Assert.False(decision.Value);
        Assert.Equal(DecisionAction.Accept, decision.Action);
        Assert.Equal(0.95, decision.Confidence, 6);
    }

    [Fact]
    public void MajorityConsensusReportsAgreementSeparatelyFromConfidence()
    {
        var result = ConsensusAggregator.Aggregate(
        [
            Decision("typesafe", Department.Billing, 0.4),
            Decision("openrouter", Department.Billing, 0.45),
            Decision("gateway", Department.Technical, 0.9),
        ]);

        Assert.Equal(Department.Billing, result.Value);
        Assert.Equal(2d / 3d, result.Agreement, 6);

        // Complete agreement at low confidence is still low confidence: the two signals stay
        // separate so a caller can require both.
        Assert.Equal(0.425, result.Confidence, 6);
        Assert.True(result.HasConsensus);
    }

    [Fact]
    public void ConfidenceWeightedConsensusCanOverrideTheMajority()
    {
        var result = ConsensusAggregator.Aggregate(
            [
                Decision("a", Department.Billing, 0.35),
                Decision("b", Department.Billing, 0.30),
                Decision("c", Department.Technical, 0.99),
            ],
            strategy: ConsensusStrategy.ConfidenceWeighted);

        Assert.Equal(Department.Technical, result.Value);
        Assert.Equal(1d / 3d, result.Agreement, 6);
    }

    [Fact]
    public void FailingTheAgreementBarIsReportedRatherThanHidden()
    {
        var result = ConsensusAggregator.Aggregate(
            [
                Decision("a", Department.Billing, 0.8),
                Decision("b", Department.Technical, 0.8),
                Decision("c", Department.Sales, 0.8),
            ],
            minimumAgreement: 0.75);

        Assert.False(result.HasConsensus);
        Assert.Equal(1d / 3d, result.Agreement, 6);
    }

    [Fact]
    public void PrimaryWithVerificationKeepsThePrimaryWhenVerifiersAgree()
    {
        var result = ConsensusAggregator.Aggregate(
            [
                Decision("primary", Department.Sales, 0.7),
                Decision("verifier", Department.Sales, 0.6),
                Decision("verifier2", Department.Billing, 0.95),
            ],
            minimumAgreement: 0.6,
            strategy: ConsensusStrategy.PrimaryWithVerification);

        Assert.Equal(Department.Sales, result.Value);
    }

    [Fact]
    public void AnEmptyConsensusIsRejected()
        => Assert.Throws<ArgumentException>(() =>
            ConsensusAggregator.Aggregate(Array.Empty<ProviderDecision<Department>>()));

    private static ProviderDecision<Department> Decision(string provider, Department value, double confidence)
        => new() { Provider = provider, Value = value, Confidence = confidence };
}
