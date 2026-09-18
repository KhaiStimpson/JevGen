using System.Text.Json.Serialization;

namespace JevGen.Runtime.Tests;

public enum Department
{
    [JevOption("billing", "Invoices, payments and refunds")]
    Billing,

    [JevOption("technical", "Defects, outages and technical support")]
    Technical,

    [JevOption("sales", "Pricing and new business")]
    Sales,
}

public sealed record Ticket
{
    public required string Subject { get; init; }

    public string? Body { get; init; }
}

public sealed record TicketAssessment
{
    [JevNoul("Does this require urgent attention?")]
    public required NoulResult Urgent { get; init; }

    [JevChoice("Which department should handle it?")]
    public required ChoiceResult<Department> Department { get; init; }

    [JevScore("Rate the severity.", Min = 1, Max = 5)]
    public required ScoreResult Severity { get; init; }
}

[JevClient(Version = "1")]
public interface ITicketAI
{
    [JevChoice("Which department should handle this ticket?")]
    Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);

    [JevNoul("Does this ticket require urgent attention?")]
    ValueTask<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);

    [JevScore("Rate the severity.", Min = 1, Max = 5)]
    Task<ScoreResult> SeverityAsync(Ticket ticket, CancellationToken cancellationToken = default);

    [JevEvaluate]
    Task<TicketAssessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);

    [DecisionPolicy(AcceptAbove = 0.9, ReviewAbove = 0.6)]
    [JevChoice("Which department should handle this ticket?")]
    Task<Decision<Department>> DecideAsync(Ticket ticket, CancellationToken cancellationToken = default);
}

[JsonSerializable(typeof(Ticket))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
