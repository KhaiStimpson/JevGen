using System.Collections.Immutable;

namespace JevGen;

/// <summary>Compile-time metadata about one method on a generated client.</summary>
public sealed record JevMethodDescriptor
{
    /// <summary>The method name.</summary>
    public required string Name { get; init; }

    /// <summary>The questions the method evaluates, in declaration order.</summary>
    public required ImmutableArray<JevQuestionDefinition> Questions { get; init; }

    /// <summary>The display name of the state type the method accepts.</summary>
    public required string StateTypeName { get; init; }

    /// <summary>The display name of the result the method produces.</summary>
    public required string ResultTypeName { get; init; }

    /// <summary>A model override declared on the method.</summary>
    public string? Model { get; init; }

    /// <summary>A provider override declared on the method.</summary>
    public string? Provider { get; init; }

    /// <summary>Everything this method needs a provider to support.</summary>
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

/// <summary>
/// Compile-time metadata about a generated client, emitted by the source generator and
/// registered from a module initializer.
/// </summary>
/// <remarks>
/// This is what lets JevGen register clients, validate capabilities at start-up and describe
/// contracts without any runtime assembly scanning or reflection over attributes.
/// </remarks>
public sealed record JevClientDescriptor
{
    /// <summary>The contract interface type.</summary>
    public required Type ContractType { get; init; }

    /// <summary>The generated implementation type.</summary>
    public required Type ImplementationType { get; init; }

    /// <summary>The contract name used in telemetry and audit records.</summary>
    public required string Name { get; init; }

    /// <summary>The methods on the contract.</summary>
    public required ImmutableArray<JevMethodDescriptor> Methods { get; init; }

    /// <summary>Creates an instance of the generated client over a runtime.</summary>
    public required Func<IEvaluationRuntime, object> Factory { get; init; }

    /// <summary>The declared contract version, when the contract declares one.</summary>
    public string? ContractVersion { get; init; }

    /// <summary>A provider override declared on the contract.</summary>
    public string? Provider { get; init; }

    /// <summary>A model override declared on the contract.</summary>
    public string? Model { get; init; }

    /// <summary>Everything this contract needs a provider to support.</summary>
    public JevCapabilitySet RequiredCapabilities
    {
        get
        {
            var capabilities = JevCapabilitySet.None;

            foreach (var method in Methods)
            {
                capabilities |= method.RequiredCapabilities;
            }

            return capabilities;
        }
    }
}
