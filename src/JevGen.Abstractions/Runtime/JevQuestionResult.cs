namespace JevGen;

/// <summary>The answer to a single question, in provider-neutral form.</summary>
public abstract record JevQuestionResult
{
    /// <summary>Creates a question result.</summary>
    protected JevQuestionResult(string questionId) => QuestionId = questionId;

    /// <summary>The identifier of the question this answers.</summary>
    public string QuestionId { get; init; }
}

/// <summary>The answer to a <see cref="JevQuestionKind.Noul"/> question.</summary>
public sealed record NoulQuestionResult : JevQuestionResult
{
    /// <summary>Creates a noul answer.</summary>
    public NoulQuestionResult(string questionId, double probability)
        : base(questionId)
        => Probability = probability;

    /// <summary>The probability that the proposition holds.</summary>
    public double Probability { get; init; }
}

/// <summary>The answer to a <see cref="JevQuestionKind.Choice"/> question.</summary>
public sealed record ChoiceQuestionResult : JevQuestionResult
{
    /// <summary>Creates a choice answer.</summary>
    public ChoiceQuestionResult(
        string questionId,
        string selectedOptionId,
        double confidence,
        IReadOnlyDictionary<string, double>? probabilities = null)
        : base(questionId)
    {
        SelectedOptionId = selectedOptionId;
        Confidence = confidence;
        Probabilities = probabilities ?? new Dictionary<string, double>();
    }

    /// <summary>The wire identifier of the option the model chose.</summary>
    public string SelectedOptionId { get; init; }

    /// <summary>Confidence in the selected option.</summary>
    public double Confidence { get; init; }

    /// <summary>The probability assigned to each option identifier.</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; init; }
}

/// <summary>The answer to a <see cref="JevQuestionKind.Score"/> question.</summary>
public sealed record ScoreQuestionResult : JevQuestionResult
{
    /// <summary>Creates a score answer.</summary>
    public ScoreQuestionResult(string questionId, double value, double? confidence = null)
        : base(questionId)
    {
        Value = value;
        Confidence = confidence;
    }

    /// <summary>The score the model produced.</summary>
    public double Value { get; init; }

    /// <summary>The model's confidence in the score, when reported.</summary>
    public double? Confidence { get; init; }
}
