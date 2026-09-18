using System.Text.Json.Serialization;
using JevGen;

namespace ContentModeration;

public enum ContentCategory
{
    [JevOption("safe", "Ordinary content with nothing that warrants action")]
    Safe,

    [JevOption("spam", "Unsolicited promotion, link farming or repetitive posting")]
    Spam,

    [JevOption("harassment", "Targeted abuse, threats or sustained hostility toward a person")]
    Harassment,

    [JevOption("self_harm", "Discussion of self-harm where the author may be at risk")]
    SelfHarm,

    [JevOption("other_violation", "Breaks the policy in a way the other categories do not cover")]
    OtherViolation,
}

public sealed record Content
{
    public required string Text { get; init; }

    public required string Surface { get; init; }

    public required int AuthorAccountAgeDays { get; init; }

    public required int AuthorPriorViolations { get; init; }
}

public sealed record ModerationAssessment
{
    [JevChoice("Which policy category best describes this content?")]
    public required ChoiceResult<ContentCategory> Category { get; init; }

    [JevNoul("Does this content require removal under the policy?")]
    public required NoulResult RequiresRemoval { get; init; }

    [JevNoul("Does this content suggest the author may be at immediate risk of harm?")]
    public required NoulResult SafetyConcern { get; init; }

    [JevScore(
        "Rate how confident a human reviewer would need to be before acting.",
        "Trivially clear",
        "Fairly clear",
        "Genuinely ambiguous",
        "Needs specialist judgement")]
    public required ScoreResult ReviewDifficulty { get; init; }
}

[JevClient(Version = "3")]
public interface IModerationAI
{
    [JevEvaluate]
    Task<ModerationAssessment> AssessAsync(Content content, CancellationToken cancellationToken = default);

    [JevChoice("Classify this content.")]
    Task<ChoiceResult<ContentCategory>> ClassifyAsync(Content content, CancellationToken cancellationToken = default);
}

[JsonSerializable(typeof(Content))]
internal sealed partial class ModerationJsonContext : JsonSerializerContext;
