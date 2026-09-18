namespace JevGen.Extensions.AI;

/// <summary>
/// A typed evaluation over arbitrary state, for callers that do not have a compile-time
/// contract to hand.
/// </summary>
/// <remarks>
/// Generated clients remain the preferred surface: they are checked at compile time and need
/// no reflection. This exists for the genuinely dynamic cases inside an agent loop, where the
/// questions are assembled at runtime.
/// </remarks>
public interface IEvaluationClient
{
    /// <summary>Evaluates questions against a state and returns the raw results.</summary>
    ValueTask<EvaluationResult> EvaluateAsync(
        object state,
        IReadOnlyList<JevQuestionDefinition> questions,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of a dynamic evaluation.</summary>
/// <param name="Results">Answers keyed by question identifier.</param>
/// <param name="Metadata">Provenance for the evaluation.</param>
public sealed record EvaluationResult(
    IReadOnlyDictionary<string, JevQuestionResult> Results,
    EvaluationMetadata Metadata)
{
    /// <summary>Reads a noul answer.</summary>
    public NoulResult Noul(string questionId)
    {
        var result = Require<NoulQuestionResult>(questionId);
        return new NoulResult(result.Probability) { Metadata = Metadata };
    }

    /// <summary>Reads a score answer.</summary>
    public ScoreResult Score(string questionId)
    {
        var result = Require<ScoreQuestionResult>(questionId);
        return new ScoreResult(result.Value, result.Confidence) { Metadata = Metadata };
    }

    /// <summary>Reads a choice answer, mapping option identifiers onto an enum.</summary>
    public ChoiceResult<TEnum> Choice<TEnum>(string questionId)
        where TEnum : struct, Enum
    {
        var result = Require<ChoiceQuestionResult>(questionId);

        if (!Enum.TryParse<TEnum>(result.SelectedOptionId, ignoreCase: true, out var value))
        {
            throw new EvaluationResponseException(
                $"The answer '{result.SelectedOptionId}' to question '{questionId}' is not a member of {typeof(TEnum).Name}.")
            {
                Provider = Metadata.Provider,
                RequestId = Metadata.RequestId,
            };
        }

        var probabilities = new Dictionary<TEnum, double>();

        foreach (var (optionId, probability) in result.Probabilities)
        {
            if (Enum.TryParse<TEnum>(optionId, ignoreCase: true, out var member))
            {
                probabilities[member] = probability;
            }
        }

        return new ChoiceResult<TEnum>(value, result.Confidence, probabilities) { Metadata = Metadata };
    }

    private T Require<T>(string questionId)
        where T : JevQuestionResult
        => Results.TryGetValue(questionId, out var result) && result is T typed
            ? typed
            : throw new EvaluationResponseException(
                $"No {typeof(T).Name} answer was returned for question '{questionId}'.")
            {
                Provider = Metadata.Provider,
                RequestId = Metadata.RequestId,
            };
}

/// <summary>The default <see cref="IEvaluationClient"/>, over the JevGen runtime.</summary>
public sealed class EvaluationClient(IEvaluationRuntime runtime) : IEvaluationClient
{
    private readonly IEvaluationRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    /// <inheritdoc />
    public async ValueTask<EvaluationResult> EvaluateAsync(
        object state,
        IReadOnlyList<JevQuestionDefinition> questions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count == 0)
        {
            throw new ArgumentException("An evaluation needs at least one question.", nameof(questions));
        }

        var response = await _runtime.EvaluateAsync(
            new EvaluationRequest
            {
                State = state,
                StateTypeInfo = JevGenJson.TryGetTypeInfo(state.GetType()),
                Questions = [.. questions],
                ClientName = nameof(EvaluationClient),
                MethodName = nameof(EvaluateAsync),
            },
            cancellationToken).ConfigureAwait(false);

        return new EvaluationResult(response.Results, response.Metadata);
    }
}
