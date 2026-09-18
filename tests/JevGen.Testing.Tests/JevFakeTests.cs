using JevGen.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JevGen.Testing.Tests;

/// <summary>
/// Covers the testing package. These tests are also the evidence that a fake exercises the
/// real generated client rather than standing in for it.
/// </summary>
public sealed class JevFakeTests
{
    private static Ticket SampleTicket => new() { Subject = "Refund for a duplicate charge" };

    [Fact]
    public async Task AFakeRunsTheRealGeneratedClient()
    {
        var fake = JevFake.Create<ITicketAI>();
        fake.Returns("route", Department.Technical, 0.93);

        var result = await fake.Client.RouteAsync(SampleTicket);

        Assert.Equal(Department.Technical, result.Value);
        Assert.Equal(0.93, result.Confidence, 6);

        // The generated client built a real request: the question metadata came from the
        // contract, not from the test.
        var request = fake.Runtime.LastRequest!;
        Assert.Equal("ITicketAI", request.ClientName);
        Assert.Equal("RouteAsync", request.MethodName);
        Assert.Equal("Which department should handle this ticket?", request.Questions[0].Prompt);
        Assert.Equal(3, request.Questions[0].Options.Length);
    }

    [Fact]
    public async Task EnumMembersAreTranslatedIntoDeclaredOptionIdentifiers()
    {
        var fake = JevFake.Create<ITicketAI>();

        // The test names the enum member; the [JevOption] identifier never has to be restated.
        fake.Returns(
            "route",
            Department.Sales,
            0.7,
            new Dictionary<Department, double>
            {
                [Department.Sales] = 0.7,
                [Department.Billing] = 0.2,
                [Department.Technical] = 0.1,
            });

        var result = await fake.Client.RouteAsync(SampleTicket);

        Assert.Equal(Department.Sales, result.Value);
        Assert.Equal(0.2, result.ProbabilityOf(Department.Billing), 6);
    }

    [Fact]
    public async Task AggregateEvaluationsCanBeScriptedPerQuestion()
    {
        var fake = JevFake.Create<ITicketAI>(runtime => runtime
            .Noul("urgent", 0.08)
            .Choice("department", "billing", 0.91)
            .Score("severity", 2, 0.77));

        var assessment = await fake.Client.AssessAsync(SampleTicket);

        Assert.Equal(0.08, assessment.Urgent.Probability, 6);
        Assert.False(assessment.Urgent.Value());
        Assert.Equal(Department.Billing, assessment.Department.Value);
        Assert.Equal(2d, assessment.Severity.Value, 6);
        Assert.Equal(0.77, assessment.Severity.Confidence, 6);
    }

    [Fact]
    public async Task ConfidenceBoundariesCanBeTestedExactly()
    {
        var fake = JevFake.Create<ITicketAI>();

        foreach (var (confidence, expected) in new[]
                 {
                     (0.9d, DecisionAction.Accept),
                     (0.89d, DecisionAction.Review),
                     (0.6d, DecisionAction.Review),
                     (0.59d, DecisionAction.Reject),
                 })
        {
            fake.Runtime.Reset();
            fake.Returns("decide", Department.Billing, confidence);

            var decision = await fake.Client.DecideAsync(SampleTicket);
            Assert.Equal(expected, decision.Action);
        }
    }

    [Fact]
    public async Task UnscriptedQuestionsAnswerWithUncertaintyRatherThanConfidence()
    {
        var fake = JevFake.Create<ITicketAI>();

        var urgent = await fake.Client.IsUrgentAsync(SampleTicket);
        var routed = await fake.Client.RouteAsync(SampleTicket);

        // An unconfigured question must never look like a confident answer in a test.
        Assert.Equal(0.5, urgent.Probability, 6);
        Assert.Equal(1d / 3d, routed.Confidence, 6);
    }

    [Fact]
    public async Task FailuresCanBeSimulated()
    {
        var fake = JevFake.Create<ITicketAI>();
        fake.Runtime.TimesOut();

        await Assert.ThrowsAsync<EvaluationTimeoutException>(() => fake.Client.RouteAsync(SampleTicket));

        fake.Runtime.RateLimited(TimeSpan.FromSeconds(5));
        var rateLimit = await Assert.ThrowsAsync<EvaluationRateLimitException>(
            () => fake.Client.RouteAsync(SampleTicket));

        Assert.Equal(TimeSpan.FromSeconds(5), rateLimit.RetryAfter);
    }

    [Fact]
    public async Task DelaysCanBeSimulatedAndCancelled()
    {
        var fake = JevFake.Create<ITicketAI>();
        fake.Runtime.Delay = TimeSpan.FromSeconds(10);

        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fake.Client.RouteAsync(SampleTicket, source.Token));
    }

    [Fact]
    public async Task AnswersCanDependOnTheStateUnderTest()
    {
        var fake = JevFake.Create<ITicketAI>();

        fake.Runtime.Answer("route", request =>
        {
            var ticket = (Ticket)request.State;
            var option = ticket.Subject.Contains("refund", StringComparison.OrdinalIgnoreCase)
                ? "billing"
                : "technical";

            return new ChoiceQuestionResult("route", option, 0.99, new Dictionary<string, double> { [option] = 0.99 });
        });

        Assert.Equal(Department.Billing, (await fake.Client.RouteAsync(SampleTicket)).Value);
        Assert.Equal(
            Department.Technical,
            (await fake.Client.RouteAsync(new Ticket { Subject = "Server is down" })).Value);
    }

    [Fact]
    public async Task FixturesReplayDeterministically()
    {
        const string Json = """
            {
              "answers": {
                "urgent": { "probability": 0.12 },
                "department": { "choice": "technical", "probabilities": { "technical": 0.88, "billing": 0.12 } },
                "severity": { "score": 4, "confidence": 0.81 }
              }
            }
            """;

        var fake = JevFake.Create<ITicketAI>().LoadFixtureJson(Json);
        var assessment = await fake.Client.AssessAsync(SampleTicket);

        Assert.Equal(0.12, assessment.Urgent.Probability, 6);
        Assert.Equal(Department.Technical, assessment.Department.Value);
        Assert.Equal(0.88, assessment.Department.Confidence, 6);
        Assert.Equal(4d, assessment.Severity.Value, 6);
    }

    [Fact]
    public async Task FixturesRoundTripThroughDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jevgen-fixture-{Guid.NewGuid():N}.json");

        try
        {
            new JevFixture
            {
                Answers = new Dictionary<string, JevFixtureAnswer>
                {
                    ["route"] = new() { Choice = "sales", Confidence = 0.66, Probabilities = new() { ["sales"] = 0.66 } },
                },
            }.Save(path);

            var fake = JevFake.Create<ITicketAI>().LoadFixture(path);
            var result = await fake.Client.RouteAsync(SampleTicket);

            Assert.Equal(Department.Sales, result.Value);
            Assert.Equal(0.66, result.Confidence, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RecordingCapturesAnswersButNeverState()
    {
        var inner = new FakeEvaluationRuntime().Choice("route", "billing", 0.94);
        var recorder = new RecordingEvaluationRuntime(inner);

        await JevClientRegistry.Create<ITicketAI>(recorder).RouteAsync(SampleTicket);

        var json = recorder.Fixture.ToJson();

        Assert.Contains("\"billing\"", json, StringComparison.Ordinal);

        // Evaluation state carries the model's input, which is exactly what should not end up
        // in a checked-in fixture.
        Assert.DoesNotContain("Refund for a duplicate charge", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FakesRegisterThroughDependencyInjection()
    {
        var services = new ServiceCollection();

        services.AddJevFake<ITicketAI>(fake => fake.Returns("route", Department.Sales, 0.72));

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Sales, result.Value);
    }

    [Fact]
    public void ScriptingAnUnknownQuestionFailsLoudly()
    {
        var fake = JevFake.Create<ITicketAI>();

        var exception = Assert.Throws<JevGenException>(
            () => fake.Returns("noSuchQuestion", Department.Billing, 0.9));

        Assert.Contains("declares no question 'noSuchQuestion'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestsAreRecordedForAssertions()
    {
        var fake = JevFake.Create<ITicketAI>();

        await fake.Client.RouteAsync(SampleTicket);
        await fake.Client.IsUrgentAsync(SampleTicket);

        Assert.Equal(2, fake.Runtime.CallCount);
        Assert.Equal(["RouteAsync", "IsUrgentAsync"], fake.Runtime.Requests.Select(r => r.MethodName));
    }
}
