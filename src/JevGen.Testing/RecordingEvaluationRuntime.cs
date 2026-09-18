namespace JevGen.Testing;

/// <summary>
/// Wraps a real runtime and records every answer, so a live evaluation can be captured once and
/// replayed as a fixture forever after.
/// </summary>
/// <remarks>
/// Only answers are recorded. Evaluation state is deliberately not captured, because it
/// routinely carries personal or otherwise sensitive data and a fixture file is exactly the
/// kind of artefact that ends up in a repository.
/// </remarks>
public sealed class RecordingEvaluationRuntime(IEvaluationRuntime inner) : IEvaluationRuntime
{
    private readonly IEvaluationRuntime _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Dictionary<string, JevFixtureAnswer> _recorded = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>The fixture built from everything recorded so far.</summary>
    public JevFixture Fixture
    {
        get
        {
            lock (_gate)
            {
                return new JevFixture
                {
                    Answers = new Dictionary<string, JevFixtureAnswer>(_recorded, StringComparer.Ordinal),
                };
            }
        }
    }

    /// <summary>Writes everything recorded so far to a fixture file.</summary>
    public void Save(string path) => Fixture.Save(path);

    /// <inheritdoc />
    public async ValueTask<EvaluationResponse> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _inner.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            foreach (var (questionId, result) in response.Results)
            {
                _recorded[questionId] = ToFixtureAnswer(result);
            }
        }

        return response;
    }

    private static JevFixtureAnswer ToFixtureAnswer(JevQuestionResult result) => result switch
    {
        NoulQuestionResult noul => new JevFixtureAnswer { Probability = noul.Probability },

        ChoiceQuestionResult choice => new JevFixtureAnswer
        {
            Choice = choice.SelectedOptionId,
            Confidence = choice.Confidence,
            Probabilities = choice.Probabilities.Count > 0
                ? new Dictionary<string, double>(choice.Probabilities, StringComparer.Ordinal)
                : null,
        },

        ScoreQuestionResult score => new JevFixtureAnswer
        {
            Score = score.Value,
            Confidence = score.Confidence,
        },

        _ => throw new JevGenException($"Cannot record a {result.GetType().Name} into a fixture."),
    };
}
