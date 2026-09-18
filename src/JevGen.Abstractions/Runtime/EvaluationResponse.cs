namespace JevGen;

/// <summary>
/// The canonical result of an evaluation, keyed by question identifier.
/// </summary>
public sealed record EvaluationResponse
{
    /// <summary>Results indexed by <see cref="JevQuestionDefinition.Id"/>.</summary>
    public required IReadOnlyDictionary<string, JevQuestionResult> Results { get; init; }

    /// <summary>Provenance for the evaluation.</summary>
    public required EvaluationMetadata Metadata { get; init; }

    /// <summary>Returns the result for a question, or throws when the provider omitted it.</summary>
    /// <exception cref="EvaluationResponseException">The provider did not answer the question.</exception>
    public JevQuestionResult Require(string questionId)
    {
        ArgumentNullException.ThrowIfNull(questionId);

        if (!Results.TryGetValue(questionId, out var result))
        {
            throw new EvaluationResponseException(
                $"The provider '{Metadata.Provider}' returned no result for question '{questionId}'.")
            {
                Provider = Metadata.Provider,
                RequestId = Metadata.RequestId,
            };
        }

        return result;
    }

    /// <summary>Returns the result for a question, requiring it to be of a specific shape.</summary>
    /// <exception cref="EvaluationResponseException">The result is missing or of the wrong shape.</exception>
    public TResult Require<TResult>(string questionId)
        where TResult : JevQuestionResult
    {
        var result = Require(questionId);

        if (result is not TResult typed)
        {
            throw new EvaluationResponseException(
                $"The provider '{Metadata.Provider}' answered question '{questionId}' with " +
                $"{result.GetType().Name} but {typeof(TResult).Name} was expected.")
            {
                Provider = Metadata.Provider,
                RequestId = Metadata.RequestId,
            };
        }

        return typed;
    }
}
