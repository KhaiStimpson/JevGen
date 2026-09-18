namespace JevGen;

/// <summary>
/// Provenance for a single evaluation: which provider and model answered, and how long it took.
/// </summary>
public sealed record EvaluationMetadata
{
    /// <summary>The name of the provider that produced the result.</summary>
    public required string Provider { get; init; }

    /// <summary>The model identifier the provider reported, when known.</summary>
    public string? Model { get; init; }

    /// <summary>The provider-assigned request identifier, when known.</summary>
    public string? RequestId { get; init; }

    /// <summary>Wall-clock duration of the evaluation.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// The number of provider attempts made, including retries and fallbacks.
    /// One for a request answered on the first try.
    /// </summary>
    public int Attempts { get; init; } = 1;

    /// <summary>Additional provider-reported properties, such as OpenRouter routing details.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; init; }
        = new Dictionary<string, object?>();
}
