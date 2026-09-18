using System.Text.Json.Serialization;
using JevGen;

namespace AspNetCoreSample;

public enum Department
{
    [JevOption("billing", "Invoices, payments, subscriptions and refunds")]
    Billing,

    [JevOption("technical", "Software defects, outages and technical support")]
    Technical,

    [JevOption("sales", "Pricing questions, upgrades and new business")]
    Sales,
}

public sealed record Ticket
{
    public required string Subject { get; init; }

    public required string Body { get; init; }
}

public sealed record TicketAssessment
{
    [JevNoul("Does this ticket require urgent attention?")]
    public required NoulResult Urgent { get; init; }

    [JevChoice("Which department should handle this ticket?")]
    public required ChoiceResult<Department> Department { get; init; }

    [JevScore("Rate the severity of this ticket.", Min = 1, Max = 5)]
    public required ScoreResult Severity { get; init; }
}

[JevClient]
public interface ITicketAI
{
    [JevChoice("Which department should handle this ticket?")]
    Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);

    [JevEvaluate]
    Task<TicketAssessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
}

/// <summary>The response shape the API returns, kept separate from the contract's result types.</summary>
public sealed record RoutingResponse(string Department, double Confidence, IReadOnlyDictionary<string, double> Probabilities);

[JsonSerializable(typeof(Ticket))]
[JsonSerializable(typeof(RoutingResponse))]
[JsonSerializable(typeof(TicketAssessment))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
