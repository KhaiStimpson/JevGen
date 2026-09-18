using System.Text.Json;
using JevGen.Providers;

namespace JevGen.Providers.Chat;

/// <summary>Parses a chat model's structured answer into canonical question results.</summary>
public static class ChatEvaluationParser
{
    /// <summary>Parses the model's JSON answer.</summary>
    /// <exception cref="EvaluationResponseException">The answer is missing, malformed or incomplete.</exception>
    public static IReadOnlyList<JevQuestionResult> Parse(
        JevProviderRequest request,
        string content,
        string providerName,
        string? requestId)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(ExtractJson(content));
        }
        catch (JsonException exception)
        {
            throw new EvaluationResponseException(
                $"Provider '{providerName}' returned content that is not valid JSON.",
                exception)
            {
                Provider = providerName,
                RequestId = requestId,
            };
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("answers", out var answers))
            {
                throw new EvaluationResponseException(
                    $"Provider '{providerName}' returned JSON without an 'answers' object.")
                {
                    Provider = providerName,
                    RequestId = requestId,
                };
            }

            var results = new List<JevQuestionResult>(request.Questions.Length);

            foreach (var question in request.Questions)
            {
                if (!answers.TryGetProperty(question.Id, out var answer))
                {
                    throw new EvaluationResponseException(
                        $"Provider '{providerName}' returned no answer for question '{question.Id}'.")
                    {
                        Provider = providerName,
                        RequestId = requestId,
                    };
                }

                results.Add(ParseAnswer(question, answer, providerName, requestId));
            }

            return results;
        }
    }

    private static JevQuestionResult ParseAnswer(
        JevQuestionDefinition question,
        JsonElement answer,
        string providerName,
        string? requestId)
    {
        switch (question.Kind)
        {
            case JevQuestionKind.Noul:
                return new NoulQuestionResult(
                    question.Id,
                    Number(answer, "probability", question, providerName, requestId));

            case JevQuestionKind.Choice:
                if (!answer.TryGetProperty("choice", out var choice) || choice.GetString() is not { } selected)
                {
                    throw Malformed(question, "a selected option", providerName, requestId);
                }

                var probabilities = ReadProbabilities(answer);

                if (question.RequiresProbabilities && probabilities.Count == 0)
                {
                    throw new EvaluationResponseException(
                        $"Question '{question.Id}' requires a probability distribution, but provider " +
                        $"'{providerName}' returned only a selected option.")
                    {
                        Provider = providerName,
                        RequestId = requestId,
                    };
                }

                var confidence = probabilities.TryGetValue(selected, out var probability)
                    ? probability
                    : answer.TryGetProperty("confidence", out var reported) && reported.TryGetDouble(out var value)
                        ? value
                        : 1d;

                return new ChoiceQuestionResult(question.Id, selected, confidence, probabilities);

            case JevQuestionKind.Score:
                return new ScoreQuestionResult(
                    question.Id,
                    Number(answer, "score", question, providerName, requestId),
                    answer.TryGetProperty("confidence", out var scoreConfidence)
                    && scoreConfidence.TryGetDouble(out var parsed)
                        ? parsed
                        : null);

            default:
                throw Malformed(question, "a recognised answer", providerName, requestId);
        }
    }

    private static Dictionary<string, double> ReadProbabilities(JsonElement answer)
    {
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);

        if (answer.TryGetProperty("probabilities", out var element) && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.TryGetDouble(out var value))
                {
                    probabilities[property.Name] = value;
                }
            }
        }

        return probabilities;
    }

    private static double Number(
        JsonElement answer,
        string name,
        JevQuestionDefinition question,
        string providerName,
        string? requestId)
        => answer.TryGetProperty(name, out var element) && element.TryGetDouble(out var value)
            ? value
            : throw Malformed(question, "a numeric '" + name + "'", providerName, requestId);

    private static EvaluationResponseException Malformed(
        JevQuestionDefinition question,
        string expected,
        string providerName,
        string? requestId)
        => new($"Provider '{providerName}' answered question '{question.Id}' without {expected}.")
        {
            Provider = providerName,
            RequestId = requestId,
        };

    /// <summary>
    /// Recovers the JSON object from content that may be wrapped in a fenced code block, which
    /// chat models produce even when asked not to.
    /// </summary>
    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('\n');
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (start > 0 && end > start)
            {
                trimmed = trimmed[(start + 1)..end].Trim();
            }
        }

        return trimmed;
    }
}
