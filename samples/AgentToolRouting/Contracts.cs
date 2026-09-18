using System.Text.Json.Serialization;
using JevGen;

namespace AgentToolRouting;

public enum AgentTool
{
    [JevOption("search", "Look something up on the public web")]
    Search,

    [JevOption("database", "Query the customer's own records and order history")]
    Database,

    [JevOption("email", "Draft or send a message on the user's behalf")]
    Email,

    [JevOption("calendar", "Read or change the user's schedule")]
    Calendar,

    [JevOption("none", "No tool is needed; answer directly")]
    None,
}

public enum ModelTarget
{
    [JevOption("small", "Short factual answers, classification and simple rewriting")]
    Small,

    [JevOption("large", "Multi-step reasoning, long context and nuanced writing")]
    Large,
}

public sealed record AgentContext
{
    public required string UserRequest { get; init; }

    public required IReadOnlyList<string> RecentTurns { get; init; }

    public required bool HasCustomerRecord { get; init; }
}

public sealed record GuardrailContext
{
    public required string UserRequest { get; init; }

    public required bool IsAuthenticated { get; init; }
}

[JevClient]
public interface IAgentRouter
{
    /// <summary>Chooses the tool that best satisfies the user's request.</summary>
    [JevChoice("Choose the tool that best satisfies the user's request.")]
    Task<ChoiceResult<AgentTool>> SelectToolAsync(AgentContext context, CancellationToken cancellationToken = default);

    /// <summary>Chooses the model best suited to the request, to control cost and latency.</summary>
    [JevChoice("Select the model best suited to this request.")]
    Task<ChoiceResult<ModelTarget>> SelectModelAsync(AgentContext context, CancellationToken cancellationToken = default);

    /// <summary>Decides whether a request should be blocked before it reaches a model.</summary>
    [JevNoul("Should this request be blocked?")]
    Task<NoulResult> ShouldBlockAsync(GuardrailContext context, CancellationToken cancellationToken = default);
}

[JsonSerializable(typeof(AgentContext))]
[JsonSerializable(typeof(GuardrailContext))]
internal sealed partial class AgentJsonContext : JsonSerializerContext;
