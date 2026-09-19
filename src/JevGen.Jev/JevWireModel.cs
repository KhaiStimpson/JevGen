using System.Text.Json;
using System.Text.Json.Serialization;

namespace JevGen.Jev;

/// <summary>
/// The System One request envelope: one state, many named questions.
/// </summary>
/// <remarks>
/// This is the schema TypeSafe publishes for Jev and that OpenRouter serves on its decisions
/// endpoint. It is generated from <c>https://api.typesafe.ai/openapi.json</c>, so every field
/// here exists upstream and nothing upstream defines is omitted.
/// </remarks>
public sealed record JevRequestPayload
{
    /// <summary>The serialized state every question is evaluated against.</summary>
    [JsonPropertyName("state")]
    public required JsonElement State { get; init; }

    /// <summary>The model to evaluate with.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>The questions, keyed by the identifier their answer comes back under.</summary>
    [JsonPropertyName("questions")]
    public required Dictionary<string, JevQuestionPayload> Questions { get; init; }
}

/// <summary>
/// One question on the wire.
/// </summary>
/// <remarks>
/// <c>criteria</c> carries a different JSON shape per question type — an object of named
/// alternatives for <c>choice</c>, an ordered array of level descriptions for <c>score</c>, and
/// the two outcome descriptions for <c>noul</c> — so the shapes are modelled as separate
/// properties here and written under the one wire name by
/// <see cref="JevQuestionPayloadConverter"/>.
/// </remarks>
[JsonConverter(typeof(JevQuestionPayloadConverter))]
public sealed record JevQuestionPayload
{
    /// <summary>The question shape: <c>noul</c>, <c>choice</c> or <c>score</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The natural-language prompt. Serialized as <c>instructions</c>.</summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Candidate alternatives for a choice question, keyed by identifier. A null description
    /// leaves the alternative to be interpreted by its name alone.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? ChoiceCriteria { get; init; }

    /// <summary>
    /// Ordered level descriptions for a score question. Each description's position is its
    /// score level, counting from zero.
    /// </summary>
    public IReadOnlyList<string>? ScoreCriteria { get; init; }

    /// <summary>Descriptions of the yes and no outcomes of a noul question.</summary>
    public JevNoulCriteria? NoulCriteria { get; init; }

    /// <summary>Builds a noul question.</summary>
    public static JevQuestionPayload Noul(string instructions, JevNoulCriteria? criteria = null)
        => new() { Type = "noul", Instructions = instructions, NoulCriteria = criteria };

    /// <summary>Builds a choice question.</summary>
    public static JevQuestionPayload Choice(string instructions, IReadOnlyDictionary<string, string?> criteria)
        => new() { Type = "choice", Instructions = instructions, ChoiceCriteria = criteria };

    /// <summary>Builds a score question.</summary>
    public static JevQuestionPayload Score(string instructions, IReadOnlyList<string> criteria)
        => new() { Type = "score", Instructions = instructions, ScoreCriteria = criteria };
}

/// <summary>Descriptions of the two outcomes of a noul question.</summary>
public sealed record JevNoulCriteria
{
    /// <summary>What counts as a yes answer.</summary>
    [JsonPropertyName("true")]
    public string? True { get; init; }

    /// <summary>What counts as a no answer.</summary>
    [JsonPropertyName("false")]
    public string? False { get; init; }
}

/// <summary>The System One response envelope.</summary>
public sealed record JevResponsePayload
{
    /// <summary>The model that answered. May be a pinned build rather than the alias asked for.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Answers keyed by question identifier.</summary>
    [JsonPropertyName("answers")]
    public Dictionary<string, JevAnswerPayload>? Answers { get; init; }

    /// <summary>Token counts, and cost where the host reports it.</summary>
    [JsonPropertyName("usage")]
    public JevUsagePayload? Usage { get; init; }

    /// <summary>The request identifier. Sent by OpenRouter; not part of the TypeSafe schema.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// The upstream provider that served the request. Sent by OpenRouter when it routes to a
    /// host; not part of the TypeSafe schema.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }
}

/// <summary>One answer on the wire. Which fields are populated follows <see cref="Type"/>.</summary>
public sealed record JevAnswerPayload
{
    /// <summary>The answer shape: <c>noul</c>, <c>choice</c> or <c>score</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The probability that a noul proposition holds.</summary>
    [JsonPropertyName("noul")]
    public double? Noul { get; init; }

    /// <summary>The identifier of the selected choice.</summary>
    [JsonPropertyName("choice")]
    public string? Choice { get; init; }

    /// <summary>
    /// The score, as a zero-based level over the question's criteria. It is the
    /// probability-weighted average of the levels, so it falls between them.
    /// </summary>
    [JsonPropertyName("score")]
    public double? Score { get; init; }

    /// <summary>The model's confidence in its answer.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    /// <summary>
    /// The probability of each alternative: keyed by choice identifier for a choice answer, and
    /// by zero-based level for a score answer.
    /// </summary>
    [JsonPropertyName("probabilities")]
    public Dictionary<string, double>? Probabilities { get; init; }

    /// <summary>
    /// A score question's criteria mapped to the levels they were assigned, keyed by level. Its
    /// size is how many levels the host actually scored against.
    /// </summary>
    [JsonPropertyName("legend")]
    public Dictionary<string, JsonElement>? Legend { get; init; }
}

/// <summary>What an evaluation consumed.</summary>
public sealed record JevUsagePayload
{
    /// <summary>Billable input tokens.</summary>
    [JsonPropertyName("input_tokens")]
    public long? InputTokens { get; init; }

    /// <summary>Output tokens used to answer the questions.</summary>
    [JsonPropertyName("output_tokens")]
    public long? OutputTokens { get; init; }

    /// <summary>
    /// What the evaluation cost, in the host's billing currency. Reported by OpenRouter; not
    /// part of the TypeSafe schema.
    /// </summary>
    [JsonPropertyName("cost")]
    public double? Cost { get; init; }
}

/// <summary>Source-generated serialization metadata for the Jev wire protocol.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JevRequestPayload))]
[JsonSerializable(typeof(JevResponsePayload))]
public sealed partial class JevJsonContext : JsonSerializerContext;
