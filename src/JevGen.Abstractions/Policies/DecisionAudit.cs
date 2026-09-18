namespace JevGen;

/// <summary>
/// An audit record for a single AI decision.
/// </summary>
/// <remarks>
/// Evaluation state is never captured here. Recording state is opt-in and handled separately,
/// because state routinely carries personal or otherwise sensitive data.
/// </remarks>
public sealed record DecisionAudit
{
    /// <summary>The contract interface the decision came from.</summary>
    public required string Contract { get; init; }

    /// <summary>The contract method the decision came from.</summary>
    public required string Method { get; init; }

    /// <summary>The provider that answered.</summary>
    public required string Provider { get; init; }

    /// <summary>The model that answered.</summary>
    public required string Model { get; init; }

    /// <summary>When the decision was made.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The confidence attached to the decision.</summary>
    public required double Confidence { get; init; }

    /// <summary>The declared contract version, when the client declares one.</summary>
    public string? ContractVersion { get; init; }

    /// <summary>The provider request identifier, when known.</summary>
    public string? RequestId { get; init; }

    /// <summary>How the decision was classified, when a policy was applied.</summary>
    public DecisionAction? Action { get; init; }
}

/// <summary>Receives decision audit records.</summary>
public interface IDecisionAuditSink
{
    /// <summary>Records one decision.</summary>
    ValueTask RecordAsync(DecisionAudit audit, CancellationToken cancellationToken = default);
}
