using System.Globalization;
using System.Text.Json;
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

    private static JevQuestionDefinition Score(
        double? minimum = null,
        double? maximum = null,
        params string[] criteria) => new()
    {
        Id = "severity",
        Kind = JevQuestionKind.Score,
        Prompt = "Rate severity.",
        Minimum = minimum,
        Maximum = maximum,
        Criteria = [.. criteria],
    };

    [Fact]
    public void QuestionsBecomeTheDocumentedWireShapes()
    {
        var payload = JevProtocol.ToPayload(
            Request(
                new JevQuestionDefinition { Id = "urgent", Kind = JevQuestionKind.Noul, Prompt = "Urgent?" },
                Choice(),
                Score(1, 4, "Low", "Medium", "High", "Critical")),
            "jev-latest");

        Assert.Equal("jev-latest", payload.Model);
        Assert.Equal(3, payload.Questions.Count);

        var noul = payload.Questions["urgent"];
        Assert.Equal("noul", noul.Type);
        Assert.Equal("Urgent?", noul.Instructions);

        // The prompt travels as `instructions`, and a choice's alternatives as a `criteria`
        // object keyed by option id.
        var choice = payload.Questions["department"];
        Assert.Equal("choice", choice.Type);
        Assert.Equal("Which department should handle it?", choice.Instructions);
        Assert.Equal("Invoices and payments", choice.ChoiceCriteria!["billing"]);

        // A score's criteria are an ordered array, one per level from zero.
        var score = payload.Questions["severity"];
        Assert.Equal("score", score.Type);
        Assert.Equal(["Low", "Medium", "High", "Critical"], score.ScoreCriteria!);
        Assert.Null(score.ChoiceCriteria);
    }

    /// <summary>
    /// The System One schema defines `type`, `instructions` and `criteria` and nothing else. A
    /// field JevGen used to send that the schema does not define is what a 404 hid until now.
    /// </summary>
    [Fact]
    public void TheSerializedRequestCarriesOnlyTheFieldsTheSchemaDefines()
    {
        var payload = JevProtocol.ToPayload(
            Request(
                new JevQuestionDefinition { Id = "urgent", Kind = JevQuestionKind.Noul, Prompt = "Urgent?" },
                Choice(),
                Score(1, 5)),
            "~typesafe/jev-latest");

        var json = JsonSerializer.Serialize(payload, JevJsonContext.Default.JevRequestPayload);
        using var document = JsonDocument.Parse(json);
        var questions = document.RootElement.GetProperty("questions");

        Assert.Equal("~typesafe/jev-latest", document.RootElement.GetProperty("model").GetString());

        foreach (var question in questions.EnumerateObject())
        {
            Assert.All(
                question.Value.EnumerateObject(),
                field => Assert.Contains(field.Name, new[] { "type", "instructions", "criteria" }));
        }

        Assert.Equal(
            "Invoices and payments",
            questions.GetProperty("department").GetProperty("criteria").GetProperty("billing").GetString());

        Assert.Equal(
            JsonValueKind.Array,
            questions.GetProperty("severity").GetProperty("criteria").ValueKind);

        // A noul carries no criteria: JevGen has no outcome descriptions to supply.
        Assert.False(questions.GetProperty("urgent").TryGetProperty("criteria", out _));
    }

    /// <summary>
    /// Rubric labels declare the scale, so they are sent as they stand and the answer's level
    /// maps back onto the one-based scale they imply.
    /// </summary>
    [Fact]
    public void ARubricIsSentAsWrittenAndItsLevelsMapBackOntoTheDeclaredScale()
    {
        var request = Request(Score(1, 3, "Whenever", "Soon", "It's on fire"));

        Assert.Equal(
            ["Whenever", "Soon", "It's on fire"],
            JevProtocol.ToPayload(request, model: null).Questions["severity"].ScoreCriteria!);

        // Level 1.12 of 0..2 sits 56% of the way up, which on a 1..3 scale is 2.12.
        var results = JevProtocol.ToResults(request, ScoreAnswer(1.12, levels: 3), "openrouter");

        Assert.Equal(2.12d, Assert.IsType<ScoreQuestionResult>(results[0]).Value, 6);
    }

    /// <summary>
    /// Bounds without a rubric still need criteria, because Jev requires them. Labelling each
    /// level with the value it stands for adds no meaning the contract did not declare.
    /// </summary>
    [Fact]
    public void BoundsWithoutARubricGetNumericLevelsThatRoundTrip()
    {
        var request = Request(Score(1, 5));

        Assert.Equal(
            ["1", "2", "3", "4", "5"],
            JevProtocol.ToPayload(request, model: null).Questions["severity"].ScoreCriteria!);

        // Level 3 of 0..4 is the top of the 1..5 scale; level 0 is its floor.
        Assert.Equal(4d, Assert.IsType<ScoreQuestionResult>(
            JevProtocol.ToResults(request, ScoreAnswer(3, levels: 5), "openrouter")[0]).Value, 6);

        Assert.Equal(1d, Assert.IsType<ScoreQuestionResult>(
            JevProtocol.ToResults(request, ScoreAnswer(0, levels: 5), "openrouter")[0]).Value, 6);
    }

    /// <summary>A wide scale is sampled rather than given one label per unit.</summary>
    [Fact]
    public void AWideScaleIsSampledAcrossACappedNumberOfLevels()
    {
        var request = Request(Score(0, 100));
        var criteria = JevProtocol.ToPayload(request, model: null).Questions["severity"].ScoreCriteria!;

        Assert.Equal(21, criteria.Count);
        Assert.Equal("0", criteria[0]);
        Assert.Equal("100", criteria[^1]);

        // Whatever the sampling, a level still maps back onto the declared bounds.
        Assert.Equal(50d, Assert.IsType<ScoreQuestionResult>(
            JevProtocol.ToResults(request, ScoreAnswer(10, levels: 21), "openrouter")[0]).Value, 6);
    }

    /// <summary>
    /// The legend reports the levels the host actually scored against, so it decides the
    /// mapping even when it disagrees with the rubric that was sent.
    /// </summary>
    [Fact]
    public void TheLegendDecidesHowManyLevelsAScoreWasMeasuredOver()
    {
        var results = JevProtocol.ToResults(
            Request(Score(1, 5)),
            ScoreAnswer(1, levels: 3),
            "openrouter");

        // Level 1 of 0..2 is the midpoint, which on a 1..5 scale is 3 — not the 2 it would be
        // if the five levels JevGen sent were assumed.
        Assert.Equal(3d, Assert.IsType<ScoreQuestionResult>(results[0]).Value, 6);
    }

    /// <summary>Confidence survives the level translation unchanged.</summary>
    [Fact]
    public void AScoreKeepsItsReportedConfidence()
    {
        var results = JevProtocol.ToResults(Request(Score(1, 5)), ScoreAnswer(2, levels: 5, confidence: 0.77), "openrouter");

        Assert.Equal(0.77d, Assert.IsType<ScoreQuestionResult>(results[0]).Confidence!.Value, 6);
    }

    /// <summary>Builds a score answer in the shape the live API returns.</summary>
    private static JevResponsePayload ScoreAnswer(double score, int levels, double? confidence = null)
        => new()
        {
            Answers = new Dictionary<string, JevAnswerPayload>
            {
                ["severity"] = new()
                {
                    Type = "score",
                    Score = score,
                    Confidence = confidence,
                    Legend = Legend(levels),
                },
            },
        };

    /// <summary>The level-to-label map the host returns alongside a score.</summary>
    private static Dictionary<string, JsonElement> Legend(int levels)
    {
        var json = "{"
            + string.Join(
                ',',
                Enumerable.Range(0, levels).Select(level =>
                    string.Create(CultureInfo.InvariantCulture, $"\"{level}\":\"Level {level}\"")))
            + "}";

        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateObject()
            .ToDictionary(level => level.Name, level => level.Value.Clone(), StringComparer.Ordinal);
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
                        Type = "choice",
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
                        Type = "choice",
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
                Answers = new Dictionary<string, JevAnswerPayload>
                {
                    ["department"] = new() { Type = "choice", Choice = "billing" },
                },
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
                Answers = new Dictionary<string, JevAnswerPayload>
                {
                    ["department"] = new() { Type = "choice", Choice = "billing" },
                },
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
                Answers = new Dictionary<string, JevAnswerPayload> { ["urgent"] = new() { Type = "score", Score = 4 } },
            },
            "typesafe"));

        Assert.Contains("'urgent'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("without a probability", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hosts do not agree on an error shape and OpenRouter's decisions endpoint has not
    /// published one, so error parsing recognises the known shapes and gives up quietly on the
    /// rest rather than failing while already reporting a failure.
    /// </summary>
    [Theory]
    // The gateway envelope.
    [InlineData("""{"error":{"message":"rate limited","code":429}}""", "rate limited")]
    // A bare string error, and a bare message.
    [InlineData("""{"error":"No endpoints found"}""", "No endpoints found")]
    [InlineData("""{"message":"model not found"}""", "model not found")]
    [InlineData("""{"detail":"Not Found"}""", "Not Found")]
    // TypeSafe's validation body, flattened to name the field that was wrong.
    [InlineData(
        """{"detail":[{"loc":["body","questions","urgency","criteria"],"msg":"Field required","type":"missing"}]}""",
        "questions.urgency.criteria: Field required")]
    public void AnErrorBodyIsReadInWhicheverShapeTheHostUsed(string body, string expected)
        => Assert.Equal(expected, JevProtocol.TryReadErrorMessage(body));

    [Theory]
    [InlineData("<html>502 Bad Gateway</html>")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"error":{"code":429}}""")]
    [InlineData("[1, 2, 3]")]
    public void AnUnrecognisedErrorBodyYieldsNothingRatherThanThrowing(string body)
        => Assert.Null(JevProtocol.TryReadErrorMessage(body));
}
