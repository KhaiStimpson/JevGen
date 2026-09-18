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
    private const string AssessmentResponse = """
        {
          "id": "req-42",
          "model": "jev-1",
          "answers": {
            "urgent": { "probability": 0.14 },
            "department": {
              "choice": "technical",
              "probabilities": { "billing": 0.07, "technical": 0.88, "sales": 0.05 }
            },
            "severity": { "score": 4, "confidence": 0.79 }
          }
        }
        """;

    private const string RouteResponse = """
        {
          "id": "req-7",
          "model": "jev-1",
          "answers": {
            "route": {
              "choice": "technical",
              "probabilities": { "billing": 0.07, "technical": 0.88, "sales": 0.05 }
            }
          }
        }
        """;

    /// <summary>A route answer with no body-level id, so the host's request-id header is used.</summary>
    private const string RouteResponseWithoutId = """
        {
          "model": "jev-1",
          "answers": {
            "route": {
              "choice": "technical",
              "probabilities": { "billing": 0.07, "technical": 0.88, "sales": 0.05 }
            }
          }
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
        Assert.Equal(4d, assessment.Severity.Value, 6);
        Assert.Equal("req-42", assessment.Severity.Metadata!.RequestId);
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

        // Enum criteria reach the model.
        Assert.Equal(
            "Defects, outages and technical support",
            questions.GetProperty("department").GetProperty("options").GetProperty("technical").GetString());

        Assert.Equal("/v1/jev/evaluate", request.Path);
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

        // The alias is translated into OpenRouter's Jev identifier.
        using var document = JsonDocument.Parse(request.Body);
        Assert.Equal("typesafe/jev-1", document.RootElement.GetProperty("model").GetString());

        // Routing detail is preserved without changing the core result contract, and the host's
        // request-id header is picked up when the body carries none.
        Assert.Equal("or-99", result.Metadata!.RequestId);
        Assert.Equal("TypeSafe", result.Metadata.Properties["openrouter.provider"]);
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
