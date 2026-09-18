using System.Collections.ObjectModel;

namespace JevGen;

/// <summary>
/// The outcome of a Jev <c>choice</c> question: the selected option, the model's confidence
/// in it, and the full probability distribution across the candidate options.
/// </summary>
/// <typeparam name="T">The choice type, normally an enum.</typeparam>
public readonly record struct ChoiceResult<T> : IAIResult
    where T : notnull
{
    private readonly IReadOnlyDictionary<T, double>? _probabilities;

    /// <summary>Creates a choice result.</summary>
    public ChoiceResult(T value, double confidence, IReadOnlyDictionary<T, double> probabilities)
    {
        Value = value;
        Confidence = confidence;
        _probabilities = probabilities;
    }

    /// <summary>The option the model selected.</summary>
    public T Value { get; init; }

    /// <summary>Confidence in <see cref="Value"/>, between 0 and 1.</summary>
    public double Confidence { get; init; }

    /// <summary>The probability assigned to every candidate option.</summary>
    public IReadOnlyDictionary<T, double> Probabilities
    {
        get => _probabilities ?? EmptyProbabilities.Instance;
        init => _probabilities = value;
    }

    /// <summary>Provenance for the evaluation that produced this result, when recorded.</summary>
    /// <remarks>
    /// Excluded from JSON. Results are frequently returned straight from an API, and provider
    /// names, model identifiers and request ids are internal operational detail rather than
    /// something to publish to callers. Read it in code; project it deliberately if you want it
    /// on the wire.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public EvaluationMetadata? Metadata { get; init; }

    /// <summary>The probability assigned to <paramref name="option"/>, or zero when absent.</summary>
    public double ProbabilityOf(T option)
        => Probabilities.TryGetValue(option, out var probability) ? probability : 0d;

    /// <summary>Deconstructs the result into its parts.</summary>
    public void Deconstruct(out T value, out double confidence, out IReadOnlyDictionary<T, double> probabilities)
    {
        value = Value;
        confidence = Confidence;
        probabilities = Probabilities;
    }

    private static class EmptyProbabilities
    {
        internal static readonly IReadOnlyDictionary<T, double> Instance =
            new ReadOnlyDictionary<T, double>(new Dictionary<T, double>());
    }
}

/// <summary>Factory helpers for <see cref="ChoiceResult{T}"/>.</summary>
public static class ChoiceResult
{
    /// <summary>
    /// Creates a choice result from a value and confidence, assigning the remaining
    /// probability mass to no other option.
    /// </summary>
    public static ChoiceResult<T> From<T>(T value, double confidence)
        where T : notnull
        => new(value, confidence, new ReadOnlyDictionary<T, double>(new Dictionary<T, double> { [value] = confidence }));

    /// <summary>
    /// Creates a choice result from a probability distribution, selecting the highest-probability
    /// option as the value.
    /// </summary>
    /// <exception cref="ArgumentException">The distribution is empty.</exception>
    public static ChoiceResult<T> FromDistribution<T>(IReadOnlyDictionary<T, double> probabilities)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(probabilities);

        if (probabilities.Count == 0)
        {
            throw new ArgumentException("A choice distribution must contain at least one option.", nameof(probabilities));
        }

        var best = default(T)!;
        var bestProbability = double.NegativeInfinity;

        foreach (var pair in probabilities)
        {
            if (pair.Value > bestProbability)
            {
                best = pair.Key;
                bestProbability = pair.Value;
            }
        }

        return new ChoiceResult<T>(best, bestProbability, probabilities);
    }
}
