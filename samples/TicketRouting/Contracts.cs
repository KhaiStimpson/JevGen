using System.Text.Json.Serialization;
using JevGen;

namespace TicketRouting;

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
    public required string CustomerTier { get; init; }
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

[JevClient(Version = "1")]
public interface ITicketAI
{
    [JevChoice("Which department should handle this ticket?")]
    Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);

    [JevNoul("Does this ticket require urgent attention?")]
    Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);

    [JevEvaluate]
    Task<TicketAssessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
}

[JsonSerializable(typeof(Ticket))]
internal sealed partial class TicketJsonContext : JsonSerializerContext;
