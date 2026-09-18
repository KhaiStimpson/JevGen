using System.Collections.Immutable;
using JevGen.Jev;
using JevGen.Providers;
using Xunit;

namespace JevGen.Runtime.Tests;

/// <summary>Covers translation between the canonical contract and the Jev wire protocol.</summary>
public sealed class ProtocolTests
{
    private static JevProviderRequest Request(params JevQuestionDefinition[] questions)
        => new()
        {
            State = new Ticket { Subject = "Double charge" },
            StateTypeInfo = JevGenJson.TryGetTypeInfo(typeof(Ticket)),
            Questions = [.. questions],
        };

    private static JevQuestionDefinition Choice(bool requireProbabilities = true) => new()
    {
        Id = "department",
        Kind = JevQuestionKind.Choice,
        Prompt = "Which department should handle it?",
        Options =
        [
            new JevChoiceOption("billing", "Invoices and payments"),
            new JevChoiceOption("technical", "Defects and outages"),
        ],
        RequiresProbabilities = requireProbabilities,
    };

    [Fact]
    public void QuestionsBecomeTheDocumentedWireShapes()
    {
        var payload = JevProtocol.ToPayload(
            Request(
                new JevQuestionDefinition { Id = "urgent", Kind = JevQuestionKind.Noul, Prompt = "Urgent?" },
                Choice(),
                new JevQuestionDefinition
                {
                    Id = "severity",
                    Kind = JevQuestionKind.Score,
                    Prompt = "Rate severity.",
                    Minimum = 1,
                    Maximum = 5,
                    Criteria = ["Low", "Medium", "High", "Critical"],
                }),
            "jev-1");

        Assert.Equal("jev-1", payload.Model);
        Assert.Equal(3, payload.Questions.Count);

        Assert.Equal("noul", payload.Questions["urgent"].Type);

        var choice = payload.Questions["department"];
        Assert.Equal("choice", choice.Type);
        Assert.Equal("Invoices and payments", choice.Options!["billing"]);
        Assert.True(choice.Probabilities);

        var score = payload.Questions["severity"];
        Assert.Equal("score", score.Type);
        Assert.Equal(1d, score.Min);
        Assert.Equal(5d, score.Max);
        Assert.Equal(["Low", "Medium", "High", "Critical"], score.Criteria!);
    }

    [Fact]
    public void StateIsSerializedWithoutReflectionWhenMetadataIsSupplied()
    {
        var payload = JevProtocol.ToPayload(Request(Choice()), model: null);

        Assert.Equal("Double charge", payload.State.GetProperty("subject").GetString());
    }

    [Fact]
    public void ChoiceConfidenceFallsBackToTheSelectedOptionProbability()
    {
        var request = Request(Choice());

        var results = JevProtocol.ToResults(
            request,
            new JevResponsePayload
            {
                Answers = new Dictionary<string, JevAnswerPayload>
                {
                    ["department"] = new()
                    {
                        Choice = "technical",
                        Probabilities = new Dictionary<string, double> { ["technical"] = 0.82, ["billing"] = 0.18 },
                    },
                },
            },
            "typesafe");

        var choice = Assert.IsType<ChoiceQuestionResult>(results[0]);
        Assert.Equal("technical", choice.SelectedOptionId);
        Assert.Equal(0.82d, choice.Confidence, 6);
    }

    [Fact]
    public void ChoiceIsInferredFromTheDistributionWhenOnlyProbabilitiesAreReturned()
    {
        var results = JevProtocol.ToResults(
            Request(Choice()),
            new JevResponsePayload
            {
                Answers = new Dictionary<string, JevAnswerPayload>
                {
                    ["department"] = new()
                    {
                        Probabilities = new Dictionary<string, double> { ["technical"] = 0.6, ["billing"] = 0.4 },
                    },
                },
            },
            "typesafe");

        Assert.Equal("technical", Assert.IsType<ChoiceQuestionResult>(results[0]).SelectedOptionId);
    }

    [Fact]
    public void AMissingDistributionIsRefusedWhenTheContractRequiresOne()
    {
        // Returning the bare choice would be a silent semantic downgrade: the caller's
        // ChoiceResult<T> promises a distribution it would not have.
        var exception = Assert.Throws<EvaluationResponseException>(() => JevProtocol.ToResults(
            Request(Choice()),
            new JevResponsePayload
            {
                Answers = new Dictionary<string, JevAnswerPayload> { ["department"] = new() { Choice = "billing" } },
            },
            "typesafe"));

        Assert.Contains("requires a probability distribution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABareChoiceIsAcceptedWhenTheContractDoesNotRequireADistribution()
    {
        var results = JevProtocol.ToResults(
            Request(Choice(requireProbabilities: false)),
            new JevResponsePayload
            {
                Answers = new Dictionary<string, JevAnswerPayload> { ["department"] = new() { Choice = "billing" } },
            },
            "typesafe");

        Assert.Equal("billing", Assert.IsType<ChoiceQuestionResult>(results[0]).SelectedOptionId);
    }

    [Fact]
    public void AnUnansweredQuestionIsReported()
    {
        var exception = Assert.Throws<EvaluationResponseException>(() => JevProtocol.ToResults(
            Request(Choice()),
            new JevResponsePayload { Answers = new Dictionary<string, JevAnswerPayload>() },
            "typesafe"));

        Assert.Contains("no answer for question 'department'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedAnswerIsReportedWithItsQuestion()
    {
        var exception = Assert.Throws<EvaluationResponseException>(() => JevProtocol.ToResults(
            Request(new JevQuestionDefinition { Id = "urgent", Kind = JevQuestionKind.Noul, Prompt = "Urgent?" }),
            new JevResponsePayload
            {
                Answers = new Dictionary<string, JevAnswerPayload> { ["urgent"] = new() { Score = 4 } },
            },
            "typesafe"));

        Assert.Contains("'urgent'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("without a probability", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnErrorBodyThatIsNotJevJsonIsHandledGracefully()
    {
        Assert.Null(JevProtocol.TryParseError("<html>502 Bad Gateway</html>"));
        Assert.Null(JevProtocol.TryParseError(string.Empty));

        var parsed = JevProtocol.TryParseError("""{"error":{"message":"rate limited","type":"rate_limit"}}""");
        Assert.Equal("rate limited", parsed!.Error!.Message);
    }
}
