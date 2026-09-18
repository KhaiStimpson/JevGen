using System.Collections.Immutable;
using System.Text.Json.Serialization.Metadata;

namespace JevGen;

/// <summary>
/// The request a generated client hands to <see cref="IEvaluationRuntime"/>.
/// </summary>
/// <remarks>
/// Generated code builds this from cached static question metadata and the caller's state
/// object. It carries no provider- or transport-specific detail.
/// </remarks>
public sealed record EvaluationRequest
{
    /// <summary>The state the questions are evaluated against.</summary>
    public required object State { get; init; }

    /// <summary>
    /// Serialization metadata for <see cref="State"/>. Generated clients supply this so that
    /// providers can serialize state without reflection, keeping the pipeline AOT- and
    /// trim-safe.
    /// </summary>
    public JsonTypeInfo? StateTypeInfo { get; init; }

    /// <summary>The questions to evaluate, all against the same state.</summary>
    public required ImmutableArray<JevQuestionDefinition> Questions { get; init; }

    /// <summary>The name of the contract interface this request came from.</summary>
    public required string ClientName { get; init; }

    /// <summary>The name of the contract method this request came from.</summary>
    public required string MethodName { get; init; }

    /// <summary>A contract version declared on the client, used for auditing and rollout tracking.</summary>
    public string? ContractVersion { get; init; }

    /// <summary>A model override declared on the method or the client.</summary>
    public string? Model { get; init; }

    /// <summary>A provider override declared on the method or the client, by registered name.</summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Provider-scoped extension data, keyed by provider name. Entries are only ever passed to
    /// the provider they name.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> ProviderOptions { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, object?>>();

    /// <summary>Free-form request metadata, such as correlation identifiers.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>The union of capabilities every question in this request requires.</summary>
    public JevCapabilitySet RequiredCapabilities
    {
        get
        {
            var capabilities = JevCapabilitySet.StructuredState;

            foreach (var question in Questions)
            {
                capabilities |= question.RequiredCapabilities;
            }

            if (Questions.Length > 1)
            {
                capabilities |= JevCapabilitySet.MultiQuestion;
            }

            if (Model is not null)
            {
                capabilities |= JevCapabilitySet.ModelSelection;
            }

            return capabilities;
        }
    }
}
