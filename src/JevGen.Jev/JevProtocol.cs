using System.Text.Json;
using JevGen.Providers;

namespace JevGen.Jev;

/// <summary>
/// Translates between JevGen's canonical provider contract and the Jev System One protocol.
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

    private static JevQuestionPayload ToQuestion(JevQuestionDefinition question)
    {
        switch (question.Kind)
        {
            case JevQuestionKind.Noul:
                return JevQuestionPayload.Noul(question.Prompt);

            case JevQuestionKind.Choice:
                if (question.Options.IsDefaultOrEmpty)
                {
                    throw new EvaluationSerializationException(
                        $"Choice question '{question.Id}' declares no options. Jev requires at least one.");
                }

                return JevQuestionPayload.Choice(
                    question.Prompt,
                    question.Options.ToDictionary(
                        option => option.Id,
                        option => option.Criteria,
                        StringComparer.Ordinal));

            case JevQuestionKind.Score:
                return JevQuestionPayload.Score(question.Prompt, JevScoreScale.For(question).Criteria);

            default:
                throw new EvaluationSerializationException(
                    $"Question '{question.Id}' has an unrecognised kind '{question.Kind}'.");
        }
    }

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
                    answer.Noul ?? throw Malformed(question, "a probability", providerName, requestId));

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
                var level = answer.Score ?? throw Malformed(question, "a score", providerName, requestId);
                var scale = JevScoreScale.For(question);

                return new ScoreQuestionResult(
                    question.Id,
                    scale.ToValue(level, LevelCount(answer, scale)),
                    answer.Confidence);

            default:
                throw Malformed(question, "a recognised answer", providerName, requestId);
        }
    }

    /// <summary>
    /// How many levels the host actually scored against.
    /// </summary>
    /// <remarks>
    /// The legend the host returns is authoritative: it reports the levels it used, which is
    /// what the answer is expressed over. The rubric that was sent is the fallback for a host
    /// that omits one.
    /// </remarks>
    private static int LevelCount(JevAnswerPayload answer, JevScoreScale scale)
    {
        if (answer.Legend is { Count: > 0 } legend)
        {
            return legend.Count;
        }

        if (answer.Probabilities is { Count: > 0 } probabilities)
        {
            return probabilities.Count;
        }

        return scale.Criteria.Length;
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

    /// <summary>
    /// Extracts a human-readable message from an error body, returning null when there is none
    /// to find.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hosts do not agree on an error shape, and OpenRouter's decisions endpoint has not
    /// published one at all. So this reads the shapes that are known — TypeSafe's FastAPI
    /// validation body, the <c>{"error":{"message":…}}</c> envelope OpenAI-style gateways use,
    /// and a bare <c>message</c> or <c>detail</c> — and gives up quietly on anything else rather
    /// than failing while already reporting a failure.
    /// </para>
    /// <para>
    /// A body that is not JSON at all, such as a proxy's HTML error page, yields null; callers
    /// fall back to the status line.
    /// </para>
    /// </remarks>
    public static string? TryReadErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return Trimmed(root.GetString());
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return Message(root, "error")
                ?? Message(root, "message")
                ?? Message(root, "detail")
                ?? Validation(root);
        }
    }

    /// <summary>Reads a field that is either a message itself or an object carrying one.</summary>
    private static string? Message(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => Trimmed(value.GetString()),

            JsonValueKind.Object when value.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                => Trimmed(message.GetString()),

            _ => null,
        };
    }

    /// <summary>Flattens a FastAPI validation body into one line naming each invalid field.</summary>
    private static string? Validation(JsonElement root)
    {
        if (!root.TryGetProperty("detail", out var detail) || detail.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var failures = new List<string>();

        foreach (var entry in detail.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("msg", out var message)
                || message.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var path = FieldPath(entry);
            failures.Add(path is null ? message.GetString()! : $"{path}: {message.GetString()}");
        }

        return failures.Count > 0 ? string.Join("; ", failures) : null;
    }

    private static string? FieldPath(JsonElement entry)
    {
        if (!entry.TryGetProperty("loc", out var location) || location.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var segments = new List<string>();

        foreach (var segment in location.EnumerateArray())
        {
            var text = segment.ValueKind switch
            {
                JsonValueKind.String => segment.GetString(),
                JsonValueKind.Number => segment.GetRawText(),
                _ => null,
            };

            // "body" is the request location every field path starts with; it says nothing
            // useful to someone reading the error.
            if (text is { Length: > 0 } && !string.Equals(text, "body", StringComparison.Ordinal))
            {
                segments.Add(text);
            }
        }

        return segments.Count > 0 ? string.Join('.', segments) : null;
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
