using System.Collections.Immutable;
using System.Globalization;

namespace JevGen.Jev;

/// <summary>
/// The declared scale of a score question, and its translation to and from Jev's levels.
/// </summary>
/// <remarks>
/// <para>
/// JevGen contracts declare a score's scale: explicit <c>Min</c> and <c>Max</c> bounds, or
/// rubric criteria, which imply a scale running from 1 to the number of labels. Jev does not
/// take bounds at all. It takes an ordered list of level descriptions and answers with a
/// <em>zero-based level</em> over them — the probability-weighted average of the levels, so it
/// falls between them.
/// </para>
/// <para>
/// Translating in both directions here is what keeps <see cref="ScoreResult.Value"/> on the
/// scale the contract declared, rather than silently handing callers a raw level that means
/// something different.
/// </para>
/// </remarks>
public readonly record struct JevScoreScale
{
    /// <summary>The fewest levels a generated rubric is given.</summary>
    private const int MinimumGeneratedLevels = 2;

    /// <summary>
    /// The most levels a generated rubric is given. A wide scale is sampled across this many
    /// levels rather than emitting one label per unit: the answer is a weighted average, so it
    /// still lands between them.
    /// </summary>
    private const int MaximumGeneratedLevels = 21;

    private JevScoreScale(double minimum, double maximum, ImmutableArray<string> criteria)
    {
        Minimum = minimum;
        Maximum = maximum;
        Criteria = criteria;
    }

    /// <summary>The inclusive lower bound the contract declared.</summary>
    public double Minimum { get; }

    /// <summary>The inclusive upper bound the contract declared.</summary>
    public double Maximum { get; }

    /// <summary>The ordered level descriptions to send, one per level from zero.</summary>
    public ImmutableArray<string> Criteria { get; }

    /// <summary>Resolves the scale and rubric of a score question.</summary>
    /// <exception cref="EvaluationSerializationException">
    /// The question declares neither bounds nor criteria, so it has no scale to map onto.
    /// </exception>
    public static JevScoreScale For(JevQuestionDefinition question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var labels = question.Criteria;

        if (labels.IsDefaultOrEmpty && question.Minimum is null && question.Maximum is null)
        {
            throw new EvaluationSerializationException(
                $"Score question '{question.Id}' declares no scale. Supply Min and Max bounds, or " +
                "ordered rubric criteria.");
        }

        // Criteria alone imply a one-based scale with one point per label, which is what the
        // contract layer documents and the generator emits.
        var minimum = question.Minimum ?? 1d;
        var maximum = question.Maximum ?? (labels.IsDefaultOrEmpty ? minimum + 1d : (double)labels.Length);

        return new JevScoreScale(
            minimum,
            maximum,
            labels.IsDefaultOrEmpty ? Generate(minimum, maximum) : labels);
    }

    /// <summary>
    /// Maps a zero-based level from Jev back onto the declared scale.
    /// </summary>
    /// <param name="level">The level the host answered with.</param>
    /// <param name="levelCount">
    /// How many levels the host scored against. Take it from the answer's legend where there is
    /// one; it is authoritative over the rubric that was sent.
    /// </param>
    public double ToValue(double level, int levelCount)
    {
        // A single level carries no information about position on the scale, so the only honest
        // answer is its lower bound.
        if (levelCount <= 1)
        {
            return Minimum;
        }

        return Minimum + (level / (levelCount - 1) * (Maximum - Minimum));
    }

    /// <summary>
    /// Generates numeric level descriptions for a question that declared bounds but no rubric.
    /// </summary>
    /// <remarks>
    /// Jev requires criteria, so a bounds-only question needs labels invented for it. Labelling
    /// each level with the scale value it stands for is the least presumptuous choice: it adds
    /// no meaning the contract did not declare, and it round-trips exactly through
    /// <see cref="ToValue"/>.
    /// </remarks>
    private static ImmutableArray<string> Generate(double minimum, double maximum)
    {
        var span = maximum - minimum;

        if (!double.IsFinite(span) || span <= 0)
        {
            // Degenerate bounds are the generator's job to reject. Reaching here means a
            // hand-built definition, and one level is the only rubric that cannot mislead.
            return [Format(minimum)];
        }

        // Clamped as a double before the cast: an enormous span would otherwise overflow the
        // conversion and land somewhere arbitrary.
        var levels = (int)Math.Clamp(
            Math.Round(span, MidpointRounding.AwayFromZero) + 1,
            MinimumGeneratedLevels,
            MaximumGeneratedLevels);

        var criteria = ImmutableArray.CreateBuilder<string>(levels);

        for (var level = 0; level < levels; level++)
        {
            criteria.Add(Format(minimum + (level / (double)(levels - 1) * span)));
        }

        return criteria.MoveToImmutable();
    }

    private static string Format(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);
}
