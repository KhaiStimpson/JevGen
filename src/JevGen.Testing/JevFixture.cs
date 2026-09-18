using System.Text.Json;
using System.Text.Json.Serialization;

namespace JevGen.Testing;

/// <summary>
/// Scripted answers stored as JSON, so realistic model behaviour can be captured once and
/// replayed deterministically.
/// </summary>
/// <example>
/// <code language="json">
/// {
///   "answers": {
///     "department": { "choice": "billing", "probabilities": { "billing": 0.94, "technical": 0.06 } },
///     "urgent": { "probability": 0.12 },
///     "severity": { "score": 3, "confidence": 0.8 }
///   }
/// }
/// </code>
/// </example>
public sealed record JevFixture
{
    /// <summary>Scripted answers, keyed by question identifier.</summary>
    [JsonPropertyName("answers")]
    public Dictionary<string, JevFixtureAnswer> Answers { get; init; } = [];

    /// <summary>Reads a fixture from disk.</summary>
    /// <exception cref="JevGenException">The file is missing or is not a valid fixture.</exception>
    public static JevFixture Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new JevGenException($"The fixture '{path}' does not exist.");
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses a fixture from JSON.</summary>
    /// <exception cref="JevGenException">The JSON is not a valid fixture.</exception>
    public static JevFixture Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            return JsonSerializer.Deserialize(json, JevFixtureJsonContext.Default.JevFixture)
                   ?? throw new JevGenException("The fixture JSON deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new JevGenException("The fixture is not valid JSON.", exception);
        }
    }

    /// <summary>Serializes this fixture to JSON.</summary>
    public string ToJson()
        => JsonSerializer.Serialize(this, JevFixtureJsonContext.Default.JevFixture);

    /// <summary>Writes this fixture to disk.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, ToJson());
    }

    /// <summary>Applies every answer in this fixture to a runtime.</summary>
    public void ApplyTo(FakeEvaluationRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        foreach (var (questionId, answer) in Answers)
        {
            if (answer.Probability is { } probability)
            {
                runtime.Noul(questionId, probability);
            }
            else if (answer.Choice is { } choice)
            {
                runtime.Choice(questionId, choice, answer.Confidence ?? 1d, answer.Probabilities);
            }
            else if (answer.Score is { } score)
            {
                runtime.Score(questionId, score, answer.Confidence);
            }
            else
            {
                throw new JevGenException(
                    $"The fixture answer for '{questionId}' sets none of probability, choice or score.");
            }
        }
    }
}

/// <summary>One scripted answer in a fixture.</summary>
public sealed record JevFixtureAnswer
{
    /// <summary>A noul probability.</summary>
    [JsonPropertyName("probability")]
    public double? Probability { get; init; }

    /// <summary>A selected choice option identifier.</summary>
    [JsonPropertyName("choice")]
    public string? Choice { get; init; }

    /// <summary>A choice probability distribution.</summary>
    [JsonPropertyName("probabilities")]
    public Dictionary<string, double>? Probabilities { get; init; }

    /// <summary>A score value.</summary>
    [JsonPropertyName("score")]
    public double? Score { get; init; }

    /// <summary>The confidence attached to the answer.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JevFixture))]
internal sealed partial class JevFixtureJsonContext : JsonSerializerContext;
