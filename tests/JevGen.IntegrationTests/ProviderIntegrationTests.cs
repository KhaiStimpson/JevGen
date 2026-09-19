using System.Text.Json;
using JevGen.Jev;
using JevGen.Providers.Local;
using JevGen.Providers.OpenRouter;
using JevGen.Providers.TypeSafe;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JevGen.IntegrationTests;

/// <summary>
/// Exercises the providers end to end over real HTTP: a generated client calls the runtime,
/// which calls a provider, which serializes, sends, receives and maps a response.
/// </summary>
public sealed class ProviderIntegrationTests
{
    /// <summary>
    /// Answers in the shape the live decisions endpoint returns: a noul is a <c>noul</c>
    /// probability, a score is a zero-based level with its legend, and every answer names its
    /// type. The severity question declares <c>Min = 1, Max = 5</c>, so it is sent five levels
    /// and level 3 is the 4 the caller sees.
    /// </summary>
    private const string AssessmentResponse = """
        {
          "id": "req-42",
          "model": "typesafe/jev-1.13-20260917",
          "provider": "TypeSafe",
          "answers": {
            "urgent": { "type": "noul", "noul": 0.14 },
            "department": {
              "type": "choice",
              "choice": "technical",
              "probabilities": { "billing": 0.07, "technical": 0.88, "sales": 0.05 },
              "confidence": 0.88
            },
            "severity": {
              "type": "score",
              "score": 3,
              "legend": { "0": "1", "1": "2", "2": "3", "3": "4", "4": "5" },
              "probabilities": { "0": 0.01, "1": 0.04, "2": 0.15, "3": 0.68, "4": 0.12 },
              "confidence": 0.79
            }
          },
          "usage": { "input_tokens": 430, "output_tokens": 79, "cost": 0.00001806 }
        }
        """;

    private const string RouteResponse = """
        {
          "id": "req-7",
          "model": "typesafe/jev-1.13-20260917",
          "answers": {
            "route": {
              "type": "choice",
              "choice": "technical",
              "probabilities": { "billing": 0.07, "technical": 0.88, "sales": 0.05 },
              "confidence": 0.88
            }
          },
          "usage": { "input_tokens": 118, "output_tokens": 12 }
        }
        """;

    /// <summary>A route answer with no body-level id, so the host's request-id header is used.</summary>
    private const string RouteResponseWithoutId = """
        {
          "model": "typesafe/jev-1.13-20260917",
          "provider": "TypeSafe",
          "answers": {
            "route": {
              "type": "choice",
              "choice": "technical",
              "probabilities": { "billing": 0.07, "technical": 0.88, "sales": 0.05 },
              "confidence": 0.88
            }
          },
          "usage": { "input_tokens": 118, "output_tokens": 12, "cost": 0.00000412 }
        }
        """;

    private static Ticket SampleTicket => new() { Subject = "Outage", Body = "The API is returning 500s." };

    private static ServiceProvider BuildTypeSafe(MockJevServer server, Action<TypeSafeJevOptions>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddTypeSafeJev(options =>
        {
            options.ApiKey = "test-key";
            options.BaseAddress = server.BaseAddress;
            extra?.Invoke(options);
        });

        services.AddJevClient<ITicketAI>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AContractRoundTripsOverRealHttp()
    {
        await using var server = new MockJevServer { Respond = _ => MockJevServer.MockResponse.Json(AssessmentResponse) };
        await using var services = BuildTypeSafe(server);

        var assessment = await services.GetRequiredService<ITicketAI>().AssessAsync(SampleTicket);

        Assert.Equal(0.14, assessment.Urgent.Probability, 6);
        Assert.Equal(Department.Technical, assessment.Department.Value);
        Assert.Equal(0.88, assessment.Department.Confidence, 6);
        Assert.Equal(0.07, assessment.Department.ProbabilityOf(Department.Billing), 6);

        // Level 3 of the five the question declared, back on the contract's 1..5 scale.
        Assert.Equal(4d, assessment.Severity.Value, 6);
        Assert.Equal("req-42", assessment.Severity.Metadata!.RequestId);

        // What the evaluation consumed comes back as metadata, not as part of the answer.
        var properties = assessment.Severity.Metadata.Properties;
        Assert.Equal(430L, properties["usage.inputTokens"]);
        Assert.Equal(79L, properties["usage.outputTokens"]);
        Assert.Equal(0.00001806, Assert.IsType<double>(properties["usage.cost"]), 12);
        Assert.Equal("TypeSafe", properties["provider"]);
    }

    [Fact]
    public async Task TheRequestOnTheWireMatchesTheDesignedShape()
    {
        await using var server = new MockJevServer { Respond = _ => MockJevServer.MockResponse.Json(AssessmentResponse) };
        await using var services = BuildTypeSafe(server);

        await services.GetRequiredService<ITicketAI>().AssessAsync(SampleTicket);

        var request = server.Requests.Single();
        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;

        // One state, three questions, exactly as the design describes.
        Assert.Equal("Outage", root.GetProperty("state").GetProperty("subject").GetString());

        var questions = root.GetProperty("questions");
        Assert.Equal(3, questions.EnumerateObject().Count());
        Assert.Equal("noul", questions.GetProperty("urgent").GetProperty("type").GetString());
        Assert.Equal("choice", questions.GetProperty("department").GetProperty("type").GetString());
        Assert.Equal("score", questions.GetProperty("severity").GetProperty("type").GetString());

        // The prompt is `instructions`, not `question`.
        Assert.Equal(
            "Does this require urgent attention?",
            questions.GetProperty("urgent").GetProperty("instructions").GetString());

        // Enum criteria reach the model, as a `criteria` object keyed by option id.
        Assert.Equal(
            "Defects, outages and technical support",
            questions.GetProperty("department").GetProperty("criteria").GetProperty("technical").GetString());

        // A score declares its levels as an ordered array. Min = 1, Max = 5 with no rubric
        // becomes five numeric levels.
        Assert.Equal(
            ["1", "2", "3", "4", "5"],
            questions.GetProperty("severity").GetProperty("criteria")
                .EnumerateArray().Select(level => level.GetString()));

        Assert.Equal("/v1/systemone", request.Path);
        Assert.Equal("Bearer test-key", request.Header("Authorization"));
    }

    [Fact]
    public async Task ApiKeysAreSentButNeverAppearInResultsOrErrors()
    {
        await using var server = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Error(500, """{"error":{"message":"internal"}}"""),
        };

        await using var services = BuildTypeSafe(server, options => options.ApiKey = "super-secret-key");

        var exception = await Assert.ThrowsAsync<EvaluationProviderException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.DoesNotContain("super-secret-key", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal("Bearer super-secret-key", server.Requests.Single().Header("Authorization"));
    }

    [Theory]
    [InlineData(401, typeof(EvaluationAuthenticationException))]
    [InlineData(403, typeof(EvaluationAuthenticationException))]
    [InlineData(429, typeof(EvaluationRateLimitException))]
    [InlineData(408, typeof(EvaluationTimeoutException))]
    [InlineData(400, typeof(EvaluationResponseException))]
    [InlineData(500, typeof(EvaluationProviderException))]
    [InlineData(503, typeof(EvaluationProviderException))]
    public async Task StatusCodesMapOntoTheErrorModel(int statusCode, Type expected)
    {
        await using var server = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Error(statusCode),
        };

        await using var services = BuildTypeSafe(server);

        var exception = await Record.ExceptionAsync(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.IsType(expected, exception);
        Assert.Equal(statusCode, ((EvaluationProviderException)exception!).StatusCode);
    }

    [Fact]
    public async Task AnUnparsableBodyIsReportedAsAResponseError()
    {
        await using var server = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Json("<html>not json</html>"),
        };

        await using var services = BuildTypeSafe(server);

        await Assert.ThrowsAsync<EvaluationResponseException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));
    }

    [Fact]
    public async Task OpenRouterSendsItsOwnHeadersAndModelIdentifier()
    {
        await using var server = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Json(
                RouteResponseWithoutId,
                new Dictionary<string, string>
                {
                    ["x-openrouter-provider"] = "TypeSafe",
                    ["x-openrouter-request-id"] = "or-99",
                }),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddOpenRouterJev(options =>
        {
            options.ApiKey = "or-key";
            options.BaseAddress = server.BaseAddress;
            options.SiteName = "JevGen tests";
            options.ProviderOrder.Add("typesafe");
        });

        services.AddJevClient<ITicketAI>().UseOpenRouter();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        var request = server.Requests.Single();
        Assert.Equal("Bearer or-key", request.Header("Authorization"));
        Assert.Equal("JevGen tests", request.Header("X-Title"));
        Assert.Equal("typesafe", request.Header("X-OpenRouter-Provider-Order"));

        // Jev is a decisions model. OpenRouter serves it on its own endpoint and rejects it on
        // chat/completions, so the path is not negotiable.
        Assert.Equal("/alpha/decisions", request.Path);

        // The alias is translated into OpenRouter's Jev identifier, tilde and all.
        using var document = JsonDocument.Parse(request.Body);
        Assert.Equal("~typesafe/jev-latest", document.RootElement.GetProperty("model").GetString());

        // Routing detail is preserved without changing the core result contract, and the host's
        // request-id header is picked up when the body carries none.
        Assert.Equal("or-99", result.Metadata!.RequestId);
        Assert.Equal("TypeSafe", result.Metadata.Properties["openrouter.provider"]);
        Assert.Equal(0.00000412, Assert.IsType<double>(result.Metadata.Properties["openrouter.cost"]), 12);
        Assert.Equal("typesafe/jev-1.13-20260917", result.Metadata.Model);
    }

    /// <summary>
    /// Anything that is not a <see cref="JevModel"/> alias is the caller naming a model, and is
    /// sent unchanged. That is how a build is pinned.
    /// </summary>
    [Fact]
    public async Task AnExplicitModelIdentifierIsSentUnchanged()
    {
        await using var server = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Json(RouteResponse),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddOpenRouterJev(options =>
        {
            options.ApiKey = "or-key";
            options.BaseAddress = server.BaseAddress;
            options.Model = "typesafe/jev-1.13-20260917";
        });

        services.AddJevClient<ITicketAI>().UseOpenRouter();

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        using var document = JsonDocument.Parse(server.Requests.Single().Body);
        Assert.Equal("typesafe/jev-1.13-20260917", document.RootElement.GetProperty("model").GetString());
    }

    /// <summary>
    /// The 404 that made every 1.0.0-preview.1 call fail still reports what the host said, now
    /// that error parsing no longer depends on one envelope shape.
    /// </summary>
    [Fact]
    public async Task AHostThatDoesNotServeThePathIsReportedWithItsOwnMessage()
    {
        await using var server = new MockJevServer { Respond = _ => MockJevServer.MockResponse.NotFound() };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);
        services.AddOpenRouterJev(options =>
        {
            options.ApiKey = "or-key";
            options.BaseAddress = server.BaseAddress;
        });

        services.AddJevClient<ITicketAI>().UseOpenRouter();

        await using var provider = services.BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<EvaluationProviderException>(
            () => provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Equal(404, exception.StatusCode);
        Assert.Contains("Not Found", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A request the host would reject is rejected here too, with the validation body the live
    /// API returns — and the message names the field, not just the status.
    /// </summary>
    [Fact]
    public async Task ARequestThatDoesNotMatchTheSchemaIsRejectedWithItsFieldNamed()
    {
        await using var server = new MockJevServer();

        using var client = new HttpClient { BaseAddress = server.BaseAddress };

        // The 1.0.0-preview.1 wire format, as it would have arrived at the live endpoint.
        using var response = await client.PostAsync(
            new Uri("v1/systemone", UriKind.Relative),
            new StringContent(
                """
                {"model":"jev-1","state":{"subject":"Outage"},
                 "questions":{"route":{"type":"choice","question":"Which department?",
                 "options":{"billing":null},"probabilities":true}}}
                """,
                System.Text.Encoding.UTF8,
                "application/json"));

        Assert.Equal(422, (int)response.StatusCode);

        // The provider flattens the validation body into a message that names each offending
        // field, so the failure says what is wrong rather than just reporting a status.
        var message = JevProtocol.TryReadErrorMessage(await response.Content.ReadAsStringAsync())!;

        Assert.Contains(
            "questions.route.question: Extra inputs are not permitted", message, StringComparison.Ordinal);
        Assert.Contains(
            "questions.route.options: Extra inputs are not permitted", message, StringComparison.Ordinal);
        Assert.Contains(
            "questions.route.probabilities: Extra inputs are not permitted", message, StringComparison.Ordinal);
        Assert.Contains("questions.route.criteria: Field required", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameContractRunsUnchangedOnADifferentHost()
    {
        await using var typeSafe = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Json(RouteResponse),
        };

        await using var selfHosted = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Json(RouteResponse),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddTypeSafeJev(options =>
        {
            options.ApiKey = "k";
            options.BaseAddress = typeSafe.BaseAddress;
        });

        services.AddLocalJev(options => options.BaseAddress = selfHosted.BaseAddress);

        // Only the registration changes. The contract, the questions and the result types do not.
        services.AddJevClient<ITicketAI>().UseLocal();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Technical, result.Value);
        Assert.Empty(typeSafe.Requests);
        Assert.Single(selfHosted.Requests);
        Assert.Equal("local", result.Metadata!.Provider);
    }

    [Fact]
    public async Task FallbackAcrossHostsWorksOverRealTransport()
    {
        await using var failing = new MockJevServer { Respond = _ => MockJevServer.MockResponse.Error(503) };
        await using var healthy = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Json(RouteResponse),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddOpenRouterJev(options =>
        {
            options.ApiKey = "k";
            options.BaseAddress = failing.BaseAddress;
        });

        services.AddTypeSafeJev(options =>
        {
            options.ApiKey = "k";
            options.BaseAddress = healthy.BaseAddress;
        });

        services.AddJevClient<ITicketAI>().UseOpenRouter().FallbackToTypeSafe();

        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal("typesafe", result.Metadata!.Provider);
        Assert.Equal(2, result.Metadata.Attempts);
        Assert.Single(failing.Requests);
        Assert.Single(healthy.Requests);
    }

    [Fact]
    public async Task AMissingApiKeyFailsBeforeAnyRequestIsSent()
    {
        await using var server = new MockJevServer();

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);
        services.AddTypeSafeJev(options => options.BaseAddress = server.BaseAddress);
        services.AddJevClient<ITicketAI>();

        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<EvaluationAuthenticationException>(
            () => provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task ProviderExtensionDataOnlyReachesTheProviderItNames()
    {
        await using var server = new MockJevServer
        {
            Respond = _ => MockJevServer.MockResponse.Json(RouteResponse),
        };

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);

        services.AddTypeSafeJev(options =>
        {
            options.ApiKey = "k";
            options.BaseAddress = server.BaseAddress;
        });

        services.AddJevClient<ITicketAI>()
            .UseTypeSafe()
            .ConfigureProvider("typesafe", options => options["header:X-TypeSafe-Experiment"] = "routing-v2")
            .ConfigureProvider("openrouter", options => options["header:X-Should-Not-Appear"] = "leaked");

        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        var request = server.Requests.Single();
        Assert.Equal("routing-v2", request.Header("X-TypeSafe-Experiment"));
        Assert.Null(request.Header("X-Should-Not-Appear"));
    }
}
