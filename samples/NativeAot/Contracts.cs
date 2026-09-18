using System.Text.Json.Serialization;
using JevGen;

namespace NativeAotSample;

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

[JevClient]
public interface ITicketAI
{
    [JevChoice("Which department should handle this ticket?")]
    Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
}

/// <summary>
/// The serializer context that makes this application AOT-safe.
/// </summary>
/// <remarks>
/// <para>
/// Source generators cannot read one another's output, so JevGen cannot emit this declaration
/// and have <c>System.Text.Json</c> process it. Declaring the context here, and naming it with
/// <c>[assembly: JevJsonContext]</c>, is the one piece of wiring a Native AOT application
/// supplies by hand.
/// </para>
/// <para>
/// Without it the application still compiles and still runs on a normal runtime, but state
/// serialization would fall back to reflection, which a native binary does not have.
/// </para>
/// </remarks>
[JsonSerializable(typeof(Ticket))]
internal sealed partial class AotJsonContext : JsonSerializerContext;
