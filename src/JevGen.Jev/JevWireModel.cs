using System.Text.Json;
using System.Text.Json.Serialization;

namespace JevGen.Jev;

/// <summary>The Jev request envelope: one state, many questions.</summary>
public sealed record JevRequestPayload
{
    /// <summary>The serialized state every question is evaluated against.</summary>
    [JsonPropertyName("state")]
    public required JsonElement State { get; init; }

    /// <summary>The questions, keyed by identifier.</summary>
    [JsonPropertyName("questions")]
    public required Dictionary<string, JevQuestionPayload> Questions { get; init; }

    /// <summary>The model to evaluate with.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }
}

/// <summary>One question on the wire.</summary>
public sealed record JevQuestionPayload
{
    /// <summary>The question shape: <c>noul</c>, <c>choice</c> or <c>score</c>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>The natural-language prompt.</summary>
    [JsonPropertyName("question")]
    public required string Question { get; init; }

    /// <summary>Candidate options for a choice question, keyed by identifier.</summary>
    [JsonPropertyName("options")]
    public Dictionary<string, string?>? Options { get; init; }

    /// <summary>The inclusive lower bound of a score scale.</summary>
    [JsonPropertyName("min")]
    public double? Min { get; init; }

    /// <summary>The inclusive upper bound of a score scale.</summary>
    [JsonPropertyName("max")]
    public double? Max { get; init; }

    /// <summary>Ordered rubric labels for a score question.</summary>
    [JsonPropertyName("criteria")]
    public string[]? Criteria { get; init; }

    /// <summary>Whether the caller needs a full probability distribution.</summary>
    [JsonPropertyName("probabilities")]
    public bool? Probabilities { get; init; }
}

/// <summary>The Jev response envelope.</summary>
public sealed record JevResponsePayload
{
    /// <summary>Answers keyed by question identifier.</summary>
    [JsonPropertyName("answers")]
    public Dictionary<string, JevAnswerPayload>? Answers { get; init; }

    /// <summary>The model that answered.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>The provider-assigned request identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>One answer on the wire.</summary>
public sealed record JevAnswerPayload
{
    /// <summary>The probability that a noul proposition holds.</summary>
    [JsonPropertyName("probability")]
    public double? Probability { get; init; }

    /// <summary>The identifier of the selected choice option.</summary>
    [JsonPropertyName("choice")]
    public string? Choice { get; init; }

    /// <summary>The probability assigned to each choice option.</summary>
    [JsonPropertyName("probabilities")]
    public Dictionary<string, double>? Probabilities { get; init; }

    /// <summary>The score a score question produced.</summary>
    [JsonPropertyName("score")]
    public double? Score { get; init; }

    /// <summary>The model's confidence in its answer.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

/// <summary>A Jev error body.</summary>
public sealed record JevErrorPayload
{
    /// <summary>The error detail.</summary>
    [JsonPropertyName("error")]
    public JevErrorDetail? Error { get; init; }
}

/// <summary>The detail of a Jev error.</summary>
public sealed record JevErrorDetail
{
    /// <summary>A human-readable message.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>A machine-readable error code.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

/// <summary>Source-generated serialization metadata for the Jev wire protocol.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JevRequestPayload))]
[JsonSerializable(typeof(JevResponsePayload))]
[JsonSerializable(typeof(JevErrorPayload))]
public sealed partial class JevJsonContext : JsonSerializerContext;
