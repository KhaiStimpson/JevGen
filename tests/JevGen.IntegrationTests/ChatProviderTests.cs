using System.Text.Json;
using JevGen.Providers;
using JevGen.Providers.Anthropic;
using JevGen.Providers.Gemini;
using JevGen.Providers.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JevGen.IntegrationTests;

/// <summary>
/// Covers running evaluation contracts on general-purpose chat models, and in particular the
/// rule that these providers must not silently substitute uncalibrated probabilities.
/// </summary>
/// <remarks>
/// These servers stand in for the OpenAI, Anthropic and Gemini chat APIs, which do not speak
/// System One, so they turn the mock's schema validation off. The Jev providers keep it on.
/// </remarks>
public sealed class ChatProviderTests
{
    private const string Answer = """
        {"answers":{"route":{"choice":"technical","probabilities":{"billing":0.1,"technical":0.85,"sales":0.05}}}}
        """;

    private static string OpenAIBody(string content)
        => JsonSerializer.Serialize(new
        {
            model = "gpt-4o-2024-08-06",
            choices = new[] { new { message = new { content } } },
        });

    private static Ticket SampleTicket => new() { Subject = "API is down" };

    [Fact]
    public async Task OpenAIRunsAContractThroughStructuredOutput()
    {
        await using var server = new MockJevServer
        {
            ValidateSchema = false,
            Respond = _ => MockJevServer.MockResponse.Json(OpenAIBody(Answer)),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddOpenAIEvaluation(options =>
        {
            options.ApiKey = "sk-test";
            options.BaseAddress = server.BaseAddress;
            options.AllowApproximateProbabilities = true;
        });

        services.AddJevClient<ITicketAI>().UseOpenAI();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Technical, result.Value);
        Assert.Equal(0.85, result.Confidence, 6);

        var request = server.Requests.Single();
        Assert.Equal("/v1/chat/completions", request.Path);
        Assert.Equal("Bearer sk-test", request.Header("Authorization"));

        // The questions are pinned by a strict JSON schema, not left to prose parsing.
        using var document = JsonDocument.Parse(request.Body);
        var format = document.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());

        var enumerated = format.GetProperty("json_schema").GetProperty("schema")
            .GetProperty("properties").GetProperty("answers")
            .GetProperty("properties").GetProperty("route")
            .GetProperty("properties").GetProperty("choice")
            .GetProperty("enum");

        Assert.Equal(["billing", "technical", "sales"], enumerated.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task AChatProviderRefusesAContractNeedingCalibratedProbabilitiesUnlessOptedIn()
    {
        await using var server = new MockJevServer
        {
            ValidateSchema = false,
            Respond = _ => MockJevServer.MockResponse.Json(OpenAIBody(Answer)),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        // AllowApproximateProbabilities defaults to false: a chat model's self-reported
        // distribution is not the calibrated one ChoiceResult<T> promises.
        services.AddOpenAIEvaluation(options =>
        {
            options.ApiKey = "sk-test";
            options.BaseAddress = server.BaseAddress;
        });

        services.AddJevClient<ITicketAI>().UseOpenAI();

        await using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<EvaluationCapabilityException>(
            () => provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.True(exception.Missing.HasFlag(JevCapabilitySet.Probabilities));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task AnthropicPinsTheAnswerWithAForcedTool()
    {
        var body = JsonSerializer.Serialize(new
        {
            model = "claude-sonnet-5",
            content = new[]
            {
                new
                {
                    type = "tool_use",
                    name = "record_evaluation",
                    input = JsonDocument.Parse(Answer).RootElement,
                },
            },
        });

        await using var server = new MockJevServer { ValidateSchema = false, Respond = _ => MockJevServer.MockResponse.Json(body) };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddAnthropicEvaluation(options =>
        {
            options.ApiKey = "ant-test";
            options.BaseAddress = server.BaseAddress;
            options.AllowApproximateProbabilities = true;
        });

        services.AddJevClient<ITicketAI>().UseAnthropic();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Technical, result.Value);

        var request = server.Requests.Single();
        Assert.Equal("ant-test", request.Header("x-api-key"));
        Assert.Equal("2023-06-01", request.Header("anthropic-version"));

        using var document = JsonDocument.Parse(request.Body);
        Assert.Equal("tool", document.RootElement.GetProperty("tool_choice").GetProperty("type").GetString());
    }

    [Fact]
    public async Task GeminiUsesItsResponseSchemaAndModelPath()
    {
        var body = JsonSerializer.Serialize(new
        {
            modelVersion = "gemini-2.5-flash",
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = Answer } } } },
            },
        });

        await using var server = new MockJevServer { ValidateSchema = false, Respond = _ => MockJevServer.MockResponse.Json(body) };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddGeminiEvaluation(options =>
        {
            options.ApiKey = "goog-test";
            options.BaseAddress = server.BaseAddress;
            options.AllowApproximateProbabilities = true;
        });

        services.AddJevClient<ITicketAI>().UseGemini();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Technical, result.Value);

        var request = server.Requests.Single();
        Assert.Equal("goog-test", request.Header("x-goog-api-key"));
        Assert.Contains("gemini-2.5-flash:generateContent", request.Path, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(request.Body);
        Assert.Equal(
            "application/json",
            document.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
    }

    [Fact]
    public async Task FencedJsonFromAChatModelIsRecovered()
    {
        // Chat models emit fenced code blocks even when told not to; failing on that would be
        // a needless outage.
        var fenced = "```json\n" + Answer + "\n```";

        await using var server = new MockJevServer
        {
            ValidateSchema = false,
            Respond = _ => MockJevServer.MockResponse.Json(OpenAIBody(fenced)),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddOpenAIEvaluation(options =>
        {
            options.ApiKey = "sk-test";
            options.BaseAddress = server.BaseAddress;
            options.AllowApproximateProbabilities = true;
        });

        services.AddJevClient<ITicketAI>().UseOpenAI();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Technical, result.Value);
    }

    [Fact]
    public async Task AnIncompleteChatAnswerIsReportedRatherThanGuessed()
    {
        await using var server = new MockJevServer
        {
            ValidateSchema = false,
            Respond = _ => MockJevServer.MockResponse.Json(OpenAIBody("""{"answers":{}}""")),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddOpenAIEvaluation(options =>
        {
            options.ApiKey = "sk-test";
            options.BaseAddress = server.BaseAddress;
            options.AllowApproximateProbabilities = true;
        });

        services.AddJevClient<ITicketAI>().UseOpenAI();

        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<EvaluationResponseException>(
            () => provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));
    }
}
