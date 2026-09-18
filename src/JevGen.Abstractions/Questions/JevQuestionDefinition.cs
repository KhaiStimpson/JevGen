using System.Collections.Immutable;

namespace JevGen;

/// <summary>
/// The provider-neutral definition of one question. Instances are produced by the source
/// generator as static, cached data and never rebuilt per request.
/// </summary>
public sealed record JevQuestionDefinition
{
    /// <summary>The stable identifier used to correlate the question with its result.</summary>
    public required string Id { get; init; }

    /// <summary>The question shape.</summary>
    public required JevQuestionKind Kind { get; init; }

    /// <summary>The natural-language prompt presented to the model.</summary>
    public required string Prompt { get; init; }

    /// <summary>Candidate options. Populated for <see cref="JevQuestionKind.Choice"/> only.</summary>
    public ImmutableArray<JevChoiceOption> Options { get; init; } = ImmutableArray<JevChoiceOption>.Empty;

    /// <summary>Inclusive lower bound of a score scale.</summary>
    public double? Minimum { get; init; }

    /// <summary>Inclusive upper bound of a score scale.</summary>
    public double? Maximum { get; init; }

    /// <summary>
    /// Ordered rubric labels for a score question, from lowest to highest. When present the
    /// scale runs from 1 to <c>Criteria.Length</c> unless bounds are given explicitly.
    /// </summary>
    public ImmutableArray<string> Criteria { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>A per-question model override.</summary>
    public string? Model { get; init; }

    /// <summary>A per-question provider override, by registered provider name.</summary>
    public string? Provider { get; init; }

    /// <summary>Whether the provider must report a full probability distribution for this question.</summary>
    public bool RequiresProbabilities { get; init; }

    /// <summary>The capabilities a provider must support to answer this question faithfully.</summary>
    public JevCapabilitySet RequiredCapabilities
    {
        get
        {
            var capabilities = Kind switch
            {
                JevQuestionKind.Noul => JevCapabilitySet.Noul,
                JevQuestionKind.Choice => JevCapabilitySet.Choice,
                JevQuestionKind.Score => JevCapabilitySet.Score,
                _ => JevCapabilitySet.None,
            };

            if (RequiresProbabilities)
            {
                capabilities |= JevCapabilitySet.Probabilities;
            }

            if (Model is not null)
            {
                capabilities |= JevCapabilitySet.ModelSelection;
            }

            return capabilities;
        }
    }
}
