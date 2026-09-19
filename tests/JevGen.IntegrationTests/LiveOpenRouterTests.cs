using System.Text.Json.Serialization;
using JevGen.Jev;
using JevGen.Providers.OpenRouter;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JevGen.IntegrationTests;

/// <summary>
/// Skips a test unless an environment variable is set, so a live-API test can live beside the
/// offline ones without turning CI red for everyone who has no key.
/// </summary>
/// <remarks>
/// The skip reason names the variable, so a skipped run says what to do rather than just
/// disappearing from the report.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresEnvironmentVariableAttribute : FactAttribute
{
    /// <summary>Runs the test only when <paramref name="name"/> holds a value.</summary>
    public RequiresEnvironmentVariableAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
        {
            Skip = $"Set {name} to run this test against the live API.";
        }
    }
}

public enum Tone
{
    [JevOption("friendly", "Warm and sincere")]
    Friendly,

    [JevOption("passive_aggressive", "Polite on the surface, hostile underneath")]
    PassiveAggressive,

    [JevOption("angry", "Openly hostile")]
    Angry,
}

public sealed record Message
{
    public required string Text { get; init; }

    public string? From { get; init; }
}

[JevClient(Version = "1")]
public interface IVibeCheck
{
    [JevChoice("What is the dominant emotional tone?")]
    Task<ChoiceResult<Tone>> ToneAsync(Message message, CancellationToken cancellationToken = default);

    [JevNoul("Is the author being sarcastic?")]
    Task<NoulResult> SarcasticAsync(Message message, CancellationToken cancellationToken = default);

    [JevScore("How urgently does this need a response?", "Whenever", "Soon", "It's on fire")]
    Task<ScoreResult> UrgencyAsync(Message message, CancellationToken cancellationToken = default);
}

[JsonSerializable(typeof(Message))]
internal sealed partial class LiveJsonContext : JsonSerializerContext;

/// <summary>
/// Runs a contract against the real OpenRouter decisions endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in: set <c>OPENROUTER_API_KEY</c> to run it. It costs a fraction of a cent per run and
/// needs network access, so it is not part of the default suite.
/// </para>
/// <para>
/// The mock server enforces the schema, but only against JevGen's own transcription of it.
/// This is the one test that would have caught 1.0.0-preview.1's 404 on the first run, and it
/// is the only one that will catch the schema moving under us.
/// </para>
/// </remarks>
public sealed class LiveOpenRouterTests
{
    private const string ApiKeyVariable = "OPENROUTER_API_KEY";

    /// <summary>The message from the verified call: polite words, hostile intent.</summary>
    private static Message SampleMessage => new()
    {
        Text = "Wow. Great job on the presentation. Really impressive how you managed to "
            + "leave out the numbers I sent you last week.",
        From = "my manager",
    };

    [RequiresEnvironmentVariable(ApiKeyVariable)]
    public async Task EveryQuestionShapeIsAnsweredByTheLiveDecisionsEndpoint()
    {
        JevGenJson.AddContext(LiveJsonContext.Default);

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddOpenRouterJev(options =>
        {
            options.ApiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
            options.Model = JevModel.Latest;
            options.SiteName = "JevGen integration tests";
            options.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddJevClient<IVibeCheck>().UseOpenRouter();

        await using var provider = services.BuildServiceProvider();
        var vibeCheck = provider.GetRequiredService<IVibeCheck>();

        var tone = await vibeCheck.ToneAsync(SampleMessage);
        var sarcastic = await vibeCheck.SarcasticAsync(SampleMessage);
        var urgency = await vibeCheck.UrgencyAsync(SampleMessage);

        // A choice comes back with the full distribution, as the schema requires.
        Assert.Equal(Tone.PassiveAggressive, tone.Value);
        Assert.Equal(3, tone.Probabilities.Count);
        Assert.Equal(1d, tone.Probabilities.Values.Sum(), 2);

        // A noul is a probability, not a label.
        Assert.InRange(sarcastic.Probability, 0.5, 1d);

        // Three rubric labels imply a 1..3 scale, and the level maps back onto it. The answer
        // is a weighted average, so it is allowed to fall between the levels.
        Assert.InRange(urgency.Value, 1d, 3d);

        // The routing and billing detail the decisions endpoint reports.
        var metadata = urgency.Metadata!;
        Assert.Equal(OpenRouterJevProvider.ProviderName, metadata.Provider);
        Assert.StartsWith("typesafe/jev-", metadata.Model, StringComparison.Ordinal);
        Assert.True(metadata.Properties.ContainsKey("usage.cost"));
        Assert.Equal("TypeSafe", metadata.Properties["provider"]);
    }
}
