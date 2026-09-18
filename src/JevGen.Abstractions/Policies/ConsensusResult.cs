namespace JevGen;

/// <summary>One provider's contribution to a consensus decision.</summary>
/// <typeparam name="T">The decided value type.</typeparam>
public sealed record ProviderDecision<T>
{
    /// <summary>The provider that produced this decision.</summary>
    public required string Provider { get; init; }

    /// <summary>The value that provider decided on.</summary>
    public required T Value { get; init; }

    /// <summary>That provider's confidence in its value.</summary>
    public required double Confidence { get; init; }

    /// <summary>The model that answered, when known.</summary>
    public string? Model { get; init; }
}

/// <summary>An aggregate decision reached by several providers evaluating the same state.</summary>
/// <typeparam name="T">The decided value type.</typeparam>
public sealed record ConsensusResult<T> : IAIResult
{
    /// <summary>The agreed value.</summary>
    public required T Value { get; init; }

    /// <summary>
    /// The share of participating providers that agreed on <see cref="Value"/>, between 0 and 1.
    /// </summary>
    public required double Agreement { get; init; }

    /// <summary>Every provider decision that fed the consensus.</summary>
    public required IReadOnlyList<ProviderDecision<T>> Decisions { get; init; }

    /// <summary>Confidence-weighted confidence across the agreeing providers.</summary>
    public double Confidence { get; init; }

    /// <summary>Whether the agreement level met the configured minimum.</summary>
    public bool HasConsensus { get; init; } = true;
}
