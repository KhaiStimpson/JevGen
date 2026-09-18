using System.Text.Json;
using JevGen.Providers;

namespace JevGen.Jev;

/// <summary>
/// Translates between JevGen's canonical provider contract and the Jev wire protocol.
/// </summary>
/// <remarks>
/// Every first-party provider shares this translation. What differs between TypeSafe,
/// OpenRouter and a self-hosted endpoint is authentication, addressing and model naming, not
/// Jev semantics.
/// </remarks>
public static class JevProtocol
{
    /// <summary>Builds the Jev request payload for a canonical provider request.</summary>
    public static JevRequestPayload ToPayload(JevProviderRequest request, string? model)
    {
        ArgumentNullException.ThrowIfNull(request);

        var questions = new Dictionary<string, JevQuestionPayload>(request.Questions.Length, StringComparer.Ordinal);

        foreach (var question in request.Questions)
        {
            questions[question.Id] = ToQuestion(question);
        }

        return new JevRequestPayload
        {
            State = request.SerializeState(JevGenJson.Options),
            Questions = questions,
            Model = model ?? request.Model,
        };
    }

    private static JevQuestionPayload ToQuestion(JevQuestionDefinition question) => question.Kind switch
    {
        JevQuestionKind.Noul => new JevQuestionPayload
        {
            Type = "noul",
            Question = question.Prompt,
        },

        JevQuestionKind.Choice => new JevQuestionPayload
        {
            Type = "choice",
            Question = question.Prompt,
            Options = question.Options.ToDictionary(
                option => option.Id,
                option => option.Criteria,
                StringComparer.Ordinal),
            Probabilities = question.RequiresProbabilities ? true : null,
        },

        JevQuestionKind.Score => new JevQuestionPayload
        {
            Type = "score",
            Question = question.Prompt,
            Min = question.Minimum,
            Max = question.Maximum,
            Criteria = question.Criteria.Length > 0 ? [.. question.Criteria] : null,
        },

        _ => throw new EvaluationSerializationException(
            $"Question '{question.Id}' has an unrecognised kind '{question.Kind}'."),
    };

    /// <summary>Maps a Jev response payload onto canonical question results.</summary>
    /// <exception cref="EvaluationResponseException">
    /// The response omits an answer, or answers with a shape the question did not ask for.
    /// </exception>
    public static IReadOnlyList<JevQuestionResult> ToResults(
        JevProviderRequest request,
        JevResponsePayload payload,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(payload);

        var answers = payload.Answers
            ?? throw new EvaluationResponseException(
                $"Provider '{providerName}' returned a response with no answers.")
            {
                Provider = providerName,
                RequestId = payload.Id,
            };

        var results = new List<JevQuestionResult>(request.Questions.Length);

        foreach (var question in request.Questions)
        {
            if (!answers.TryGetValue(question.Id, out var answer))
            {
                throw new EvaluationResponseException(
                    $"Provider '{providerName}' returned no answer for question '{question.Id}'.")
                {
                    Provider = providerName,
                    RequestId = payload.Id,
                };
            }

            results.Add(ToResult(question, answer, providerName, payload.Id));
        }

        return results;
    }

    private static JevQuestionResult ToResult(
        JevQuestionDefinition question,
        JevAnswerPayload answer,
        string providerName,
        string? requestId)
    {
        switch (question.Kind)
        {
            case JevQuestionKind.Noul:
                return new NoulQuestionResult(
                    question.Id,
                    answer.Probability
                        ?? throw Malformed(question, "a probability", providerName, requestId));

            case JevQuestionKind.Choice:
                var probabilities = answer.Probabilities;
                var choice = answer.Choice ?? Highest(probabilities);

                if (choice is null)
                {
                    throw Malformed(question, "a selected option", providerName, requestId);
                }

                if (question.RequiresProbabilities && (probabilities is null || probabilities.Count == 0))
                {
                    // The contract asked for a distribution. Returning the choice alone would be
                    // a silent semantic downgrade, which JevGen never does.
                    throw new EvaluationResponseException(
                        $"Question '{question.Id}' requires a probability distribution, but provider " +
                        $"'{providerName}' returned only a selected option.")
                    {
                        Provider = providerName,
                        RequestId = requestId,
                    };
                }

                var confidence = answer.Confidence
                    ?? (probabilities is not null && probabilities.TryGetValue(choice, out var probability)
                        ? probability
                        : 1d);

                return new ChoiceQuestionResult(question.Id, choice, confidence, probabilities);

            case JevQuestionKind.Score:
                return new ScoreQuestionResult(
                    question.Id,
                    answer.Score ?? throw Malformed(question, "a score", providerName, requestId),
                    answer.Confidence);

            default:
                throw Malformed(question, "a recognised answer", providerName, requestId);
        }
    }

    private static string? Highest(Dictionary<string, double>? probabilities)
    {
        if (probabilities is null || probabilities.Count == 0)
        {
            return null;
        }

        string? best = null;
        var bestProbability = double.NegativeInfinity;

        foreach (var pair in probabilities)
        {
            if (pair.Value > bestProbability)
            {
                best = pair.Key;
                bestProbability = pair.Value;
            }
        }

        return best;
    }

    private static EvaluationResponseException Malformed(
        JevQuestionDefinition question,
        string expected,
        string providerName,
        string? requestId)
        => new($"Provider '{providerName}' answered {question.Kind.ToString().ToLowerInvariant()} question " +
               $"'{question.Id}' without {expected}.")
        {
            Provider = providerName,
            RequestId = requestId,
        };

    /// <summary>Parses a Jev error body, returning null when the body is not one.</summary>
    public static JevErrorPayload? TryParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(body, JevJsonContext.Default.JevErrorPayload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
