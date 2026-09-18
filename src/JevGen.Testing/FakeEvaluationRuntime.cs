using System.Collections.Concurrent;

namespace JevGen.Testing;

/// <summary>
/// A runtime that answers from a script instead of calling a provider.
/// </summary>
/// <remarks>
/// Because generated clients depend only on <see cref="IEvaluationRuntime"/>, substituting
/// this gives a real client — real request construction, real result mapping, real enum option
/// handling — with deterministic answers. Tests exercise the generated code rather than a mock
/// of it.
/// </remarks>
public sealed class FakeEvaluationRuntime : IEvaluationRuntime
{
    private readonly List<ScriptedAnswer> _answers = [];
    private readonly ConcurrentQueue<EvaluationRequest> _requests = new();

    /// <summary>The provider name reported in result metadata.</summary>
    public string ProviderName { get; set; } = "fake";

    /// <summary>The model name reported in result metadata.</summary>
    public string ModelName { get; set; } = "fake-model";

    /// <summary>An artificial delay applied before answering, for timeout tests.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>A failure to raise instead of answering, for error-handling tests.</summary>
    public Func<EvaluationRequest, Exception?>? Failure { get; set; }

    /// <summary>Every request the fake has received, in order.</summary>
    public IReadOnlyCollection<EvaluationRequest> Requests => _requests;

    /// <summary>The most recent request, or null when nothing has been evaluated.</summary>
    public EvaluationRequest? LastRequest => _requests.LastOrDefault();

    /// <summary>How many evaluations the fake has served.</summary>
    public int CallCount => _requests.Count;

    /// <summary>Scripts an answer for a question.</summary>
    public FakeEvaluationRuntime Answer(string questionId, JevQuestionResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        ArgumentNullException.ThrowIfNull(result);

        _answers.Add(new ScriptedAnswer(questionId, null, _ => result));
        return this;
    }

    /// <summary>Scripts an answer for a question on one method only.</summary>
    public FakeEvaluationRuntime Answer(string methodName, string questionId, JevQuestionResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        ArgumentNullException.ThrowIfNull(result);

        _answers.Add(new ScriptedAnswer(questionId, methodName, _ => result));
        return this;
    }

    /// <summary>Scripts an answer computed from the request, for state-dependent tests.</summary>
    public FakeEvaluationRuntime Answer(
        string questionId,
        Func<EvaluationRequest, JevQuestionResult> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        ArgumentNullException.ThrowIfNull(factory);

        _answers.Add(new ScriptedAnswer(questionId, null, factory));
        return this;
    }

    /// <summary>Scripts a noul answer.</summary>
    public FakeEvaluationRuntime Noul(string questionId, double probability)
        => Answer(questionId, new NoulQuestionResult(questionId, probability));

    /// <summary>Scripts a choice answer with an explicit distribution.</summary>
    public FakeEvaluationRuntime Choice(
        string questionId,
        string selectedOptionId,
        double confidence,
        IReadOnlyDictionary<string, double>? probabilities = null)
        => Answer(
            questionId,
            new ChoiceQuestionResult(
                questionId,
                selectedOptionId,
                confidence,
                probabilities ?? new Dictionary<string, double> { [selectedOptionId] = confidence }));

    /// <summary>Scripts a score answer.</summary>
    public FakeEvaluationRuntime Score(string questionId, double value, double? confidence = null)
        => Answer(questionId, new ScoreQuestionResult(questionId, value, confidence));

    /// <summary>Makes the next and every later evaluation fail with <paramref name="exception"/>.</summary>
    public FakeEvaluationRuntime Fails(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Failure = _ => exception;
        return this;
    }

    /// <summary>Makes evaluations fail as if the provider timed out.</summary>
    public FakeEvaluationRuntime TimesOut()
        => Fails(new EvaluationTimeoutException("The fake runtime was configured to time out.")
        {
            Provider = ProviderName,
        });

    /// <summary>Makes evaluations fail as if the provider rate-limited the caller.</summary>
    public FakeEvaluationRuntime RateLimited(TimeSpan? retryAfter = null)
        => Fails(new EvaluationRateLimitException("The fake runtime was configured to rate limit.")
        {
            Provider = ProviderName,
            RetryAfter = retryAfter,
        });

    /// <summary>Clears recorded requests and scripted answers.</summary>
    public void Reset()
    {
        _answers.Clear();
        _requests.Clear();
        Failure = null;
        Delay = TimeSpan.Zero;
    }

    /// <inheritdoc />
    public async ValueTask<EvaluationResponse> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _requests.Enqueue(request);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Failure?.Invoke(request) is { } failure)
        {
            throw failure;
        }

        var results = new Dictionary<string, JevQuestionResult>(request.Questions.Length, StringComparer.Ordinal);

        foreach (var question in request.Questions)
        {
            results[question.Id] = Resolve(request, question);
        }

        return new EvaluationResponse
        {
            Results = results,
            Metadata = new EvaluationMetadata
            {
                Provider = ProviderName,
                Model = request.Model ?? ModelName,
                RequestId = "fake-" + _requests.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        };
    }

    private JevQuestionResult Resolve(EvaluationRequest request, JevQuestionDefinition question)
    {
        foreach (var answer in _answers)
        {
            if (!string.Equals(answer.QuestionId, question.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (answer.MethodName is { } method
                && !string.Equals(method, request.MethodName, StringComparison.Ordinal))
            {
                continue;
            }

            return answer.Factory(request);
        }

        return Default(question);
    }

    /// <summary>
    /// The answer used for a question nothing scripted: the least committal value the question
    /// allows, so an unconfigured question shows up as uncertainty rather than as a confident
    /// wrong answer.
    /// </summary>
    private static JevQuestionResult Default(JevQuestionDefinition question) => question.Kind switch
    {
        JevQuestionKind.Noul => new NoulQuestionResult(question.Id, 0.5d),

        JevQuestionKind.Choice when question.Options.Length > 0
            => new ChoiceQuestionResult(
                question.Id,
                question.Options[0].Id,
                1d / question.Options.Length,
                question.Options.ToDictionary(
                    option => option.Id,
                    _ => 1d / question.Options.Length,
                    StringComparer.Ordinal)),

        JevQuestionKind.Score => new ScoreQuestionResult(
            question.Id,
            ((question.Minimum ?? 0d) + (question.Maximum ?? 1d)) / 2d,
            0.5d),

        _ => throw new JevGenException(
            $"The fake runtime has no scripted answer for question '{question.Id}' and cannot " +
            $"invent one for a {question.Kind} question with no options."),
    };

    private sealed record ScriptedAnswer(
        string QuestionId,
        string? MethodName,
        Func<EvaluationRequest, JevQuestionResult> Factory);
}
