using System.Text.Json.Serialization;

namespace JevGen.AotTests;

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

    [JevEvaluate]
    Task<TicketAssessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
}

/// <summary>
/// The serializer context that keeps state serialization reflection-free.
/// </summary>
/// <remarks>
/// Source generators cannot see each other's output, so JevGen cannot emit this. Declaring it
/// here and naming it with <c>[assembly: JevJsonContext]</c> is what makes the application
/// AOT-safe; without it the runtime would need reflection that a native binary does not have.
/// </remarks>
[JsonSerializable(typeof(Ticket))]
internal sealed partial class AotJsonContext : JsonSerializerContext;
