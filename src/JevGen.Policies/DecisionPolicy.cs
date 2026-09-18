namespace JevGen;

/// <summary>
/// Confidence thresholds that classify a model result.
/// </summary>
/// <remarks>
/// <para>
/// A policy classifies. It does not act. Applying one to a result yields a
/// <see cref="Decision{T}"/> carrying a <see cref="DecisionAction"/>; deciding what an
/// <see cref="DecisionAction.Accept"/> means for the business remains entirely the
/// application's responsibility. Keeping evaluation separate from control flow is what makes
/// the boundary auditable.
/// </para>
/// <para>
/// Policies can be declared with <c>[DecisionPolicy]</c>, but configuring them
/// programmatically is usually better: thresholds are business rules, and they change on a
/// different schedule from the code that generates the client.
/// </para>
/// </remarks>
/// <typeparam name="T">The decided value type.</typeparam>
public sealed record DecisionPolicy<T>
{
    /// <summary>Confidence at or above which a result is accepted.</summary>
    public double AcceptThreshold { get; init; } = 0.9d;

    /// <summary>Confidence at or above which a result is sent for review.</summary>
    public double ReviewThreshold { get; init; } = 0.65d;

    /// <summary>The default policy: accept at 0.90, review from 0.65.</summary>
    public static DecisionPolicy<T> Default { get; } = new();

    /// <summary>Starts a policy that accepts at or above <paramref name="threshold"/>.</summary>
    public static DecisionPolicy<T> AcceptAbove(double threshold)
        => new DecisionPolicy<T> { AcceptThreshold = Validate(threshold, nameof(threshold)) }.Normalize();

    /// <summary>Sets the review band, between <paramref name="lower"/> and <paramref name="upper"/>.</summary>
    public DecisionPolicy<T> ReviewBetween(double lower, double upper)
    {
        Validate(lower, nameof(lower));
        Validate(upper, nameof(upper));

        if (lower >= upper)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lower), lower, "The lower bound of a review band must be below its upper bound.");
        }

        return new DecisionPolicy<T> { AcceptThreshold = upper, ReviewThreshold = lower };
    }

    /// <summary>Sets the confidence below which a result is rejected.</summary>
    public DecisionPolicy<T> RejectBelow(double threshold)
        => (this with { ReviewThreshold = Validate(threshold, nameof(threshold)) }).Normalize();

    /// <summary>Classifies a confidence level.</summary>
    public DecisionAction Classify(double confidence) => confidence >= AcceptThreshold
        ? DecisionAction.Accept
        : confidence >= ReviewThreshold
            ? DecisionAction.Review
            : DecisionAction.Reject;

    /// <summary>Applies this policy to a value and its confidence.</summary>
    public Decision<T> Apply(T value, double confidence, EvaluationMetadata? metadata = null)
        => new(value, confidence, Classify(confidence)) { Metadata = metadata };

    private DecisionPolicy<T> Normalize()
    {
        // A review threshold above the accept threshold has no meaningful reading; collapse it
        // so classification stays total rather than silently producing an empty band.
        return ReviewThreshold > AcceptThreshold
            ? this with { ReviewThreshold = AcceptThreshold }
            : this;
    }

    private static double Validate(double value, string name)
        => value is >= 0d and <= 1d
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "A confidence threshold must be between 0 and 1.");
}

/// <summary>Non-generic entry points for building decision policies.</summary>
public static class DecisionPolicy
{
    /// <summary>Starts a policy that accepts at or above <paramref name="threshold"/>.</summary>
    public static DecisionPolicy<T> AcceptAbove<T>(double threshold) => DecisionPolicy<T>.AcceptAbove(threshold);
}
