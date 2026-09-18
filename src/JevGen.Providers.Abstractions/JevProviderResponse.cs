using System.Collections.Immutable;

namespace JevGen.Providers;

/// <summary>
/// The canonical response a provider returns. It preserves probabilities, score data and
/// provider metadata without leaking provider-specific types into application contracts.
/// </summary>
public sealed record JevProviderResponse
{
    /// <summary>The answer to every question in the request.</summary>
    public required ImmutableArray<JevQuestionResult> Results { get; init; }

    /// <summary>Which provider and model answered, and any provider-specific detail.</summary>
    public required JevProviderMetadata Metadata { get; init; }

    /// <summary>Creates a response from a sequence of results.</summary>
    public static JevProviderResponse Create(
        JevProviderMetadata metadata,
        IEnumerable<JevQuestionResult> results)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(results);

        return new JevProviderResponse
        {
            Results = [.. results],
            Metadata = metadata,
        };
    }
}
