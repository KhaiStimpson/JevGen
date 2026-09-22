using JevGen.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JevGen.Runtime.Tests;

/// <summary>Covers retry, timeout, the circuit breaker and what must never be retried.</summary>
public sealed class ResilienceTests
{
    private static Ticket SampleTicket => new() { Subject = "Refund" };

    private static ServiceProvider Build(
        IJevProvider provider,
        Action<JevResilienceOptions>? resilience = null)
    {
        var services = new ServiceCollection();

        services.AddJevGen(options => options.ValidateOnStart = false)
            .AddResilience(configured =>
            {
                configured.BaseDelay = TimeSpan.FromMilliseconds(1);
                configured.UseJitter = false;
                resilience?.Invoke(configured);
            });

        services.AddSingleton(provider);
        services.AddJevClient<ITicketAI>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task TransientFailuresAreRetried()
    {
        var provider = new FlakyProvider(failuresBeforeSuccess: 2);

        await using var services = Build(provider, options => options.MaxRetries = 3);

        var result = await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Billing, result.Value);
        Assert.Equal(3, provider.CallCount);
    }

    [Fact]
    public async Task RetriesStopAtTheConfiguredLimit()
    {
        var provider = new FlakyProvider(failuresBeforeSuccess: 10);

        await using var services = Build(provider, options => options.MaxRetries = 2);

        await Assert.ThrowsAsync<EvaluationProviderException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        // The first attempt plus two retries.
        Assert.Equal(3, provider.CallCount);
    }

    [Fact]
    public async Task AuthenticationFailuresAreNeverRetried()
    {
        var provider = new FakeProvider("auth")
        {
            Failure = new EvaluationAuthenticationException("bad key") { Provider = "auth" },
        };

        await using var services = Build(provider, options => options.MaxRetries = 5);

        await Assert.ThrowsAsync<EvaluationAuthenticationException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task MalformedResponsesAreNeverRetried()
    {
        var provider = new FakeProvider("broken")
        {
            Failure = new EvaluationResponseException("garbage") { Provider = "broken" },
        };

        await using var services = Build(provider, options => options.MaxRetries = 5);

        await Assert.ThrowsAsync<EvaluationResponseException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task ARateLimitRetryAfterIsHonouredOverTheBackoffCurve()
    {
        var provider = new FlakyProvider(
            failuresBeforeSuccess: 1,
            failure: () => new EvaluationRateLimitException("slow down")
            {
                Provider = "flaky",
                RetryAfter = TimeSpan.FromMilliseconds(30),
            });

        await using var services = Build(provider, options =>
        {
            options.MaxRetries = 2;
            options.BaseDelay = TimeSpan.FromMilliseconds(1);
        });

        var started = System.Diagnostics.Stopwatch.StartNew();
        await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);
        started.Stop();

        Assert.Equal(2, provider.CallCount);
        Assert.True(
            started.Elapsed >= TimeSpan.FromMilliseconds(25),
            $"Expected the provider's Retry-After to be honoured, but the retry happened after {started.Elapsed}.");
    }

    [Fact]
    public async Task TheCircuitOpensAfterConsecutiveFailures()
    {
        var provider = new FlakyProvider(failuresBeforeSuccess: int.MaxValue);

        await using var services = Build(provider, options =>
        {
            options.MaxRetries = 0;
            options.CircuitBreakerThreshold = 2;
            options.CircuitBreakerDuration = TimeSpan.FromMinutes(1);
        });

        var client = services.GetRequiredService<ITicketAI>();

        await Assert.ThrowsAsync<EvaluationProviderException>(() => client.RouteAsync(SampleTicket));
        await Assert.ThrowsAsync<EvaluationProviderException>(() => client.RouteAsync(SampleTicket));

        var blocked = await Assert.ThrowsAsync<EvaluationCircuitOpenException>(() => client.RouteAsync(SampleTicket));

        Assert.Contains("circuit", blocked.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(blocked.IsTransient);
        Assert.NotNull(blocked.OpenUntil);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task AnOpenCircuitFallsBackToTheNextProvider()
    {
        var flaky = new FlakyProvider(failuresBeforeSuccess: int.MaxValue);
        var healthy = new FakeProvider("healthy") { SelectedOption = "sales" };

        var services = new ServiceCollection();

        services.AddJevGen(options => options.ValidateOnStart = false)
            .AddResilience(options =>
            {
                options.MaxRetries = 0;
                options.CircuitBreakerThreshold = 1;
                options.CircuitBreakerDuration = TimeSpan.FromMinutes(1);
            });

        services.AddSingleton<IJevProvider>(flaky);
        services.AddSingleton<IJevProvider>(healthy);
        services.AddJevClient<ITicketAI>().UseProvider("flaky").FallbackTo("healthy");

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ITicketAI>();

        // The first call trips the breaker and falls back; the second skips the open circuit
        // without calling the failing provider, and still falls back rather than failing.
        await client.RouteAsync(SampleTicket);
        var result = await client.RouteAsync(SampleTicket);

        Assert.Equal(Department.Sales, result.Value);
        Assert.Equal(1, flaky.CallCount);
        Assert.Equal(2, healthy.CallCount);
    }

    [Fact]
    public async Task AHalfOpenCircuitLetsExactlyOneProbeThrough()
    {
        var provider = new FlakyProvider(failuresBeforeSuccess: int.MaxValue);

        await using var services = Build(provider, options =>
        {
            options.MaxRetries = 0;
            options.CircuitBreakerThreshold = 1;
            options.CircuitBreakerDuration = TimeSpan.FromMilliseconds(500);
        });

        var client = services.GetRequiredService<ITicketAI>();

        await Assert.ThrowsAsync<EvaluationProviderException>(() => client.RouteAsync(SampleTicket));
        await Task.Delay(TimeSpan.FromMilliseconds(600));

        // The first caller after the window is the probe; it fails, which keeps the circuit
        // open, so the callers behind it never reach the provider.
        await Assert.ThrowsAsync<EvaluationProviderException>(() => client.RouteAsync(SampleTicket));
        await Assert.ThrowsAsync<EvaluationCircuitOpenException>(() => client.RouteAsync(SampleTicket));
        await Assert.ThrowsAsync<EvaluationCircuitOpenException>(() => client.RouteAsync(SampleTicket));

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task AddingResilienceTwiceDoesNotNestRetries()
    {
        var provider = new FlakyProvider(failuresBeforeSuccess: int.MaxValue);

        var services = new ServiceCollection();
        var builder = services.AddJevGen(options => options.ValidateOnStart = false);

        builder.AddResilience(options =>
        {
            options.MaxRetries = 1;
            options.BaseDelay = TimeSpan.FromMilliseconds(1);
            options.CircuitBreakerThreshold = 0;
        });
        builder.AddResilience();

        services.AddSingleton<IJevProvider>(provider);
        services.AddJevClient<ITicketAI>().AddResilience();

        await using var built = services.BuildServiceProvider();

        Assert.Single(built.GetServices<IEvaluationFilter>().OfType<ResilienceEvaluationFilter>());

        await Assert.ThrowsAsync<EvaluationProviderException>(
            () => built.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public void TheDefaultClassifierMatchesTheDocumentedPolicy()
    {
        Assert.False(JevResilienceOptions.IsTransient(new EvaluationAuthenticationException()));
        Assert.False(JevResilienceOptions.IsTransient(new EvaluationResponseException()));
        Assert.False(JevResilienceOptions.IsTransient(new EvaluationSerializationException()));
        Assert.False(JevResilienceOptions.IsTransient(new EvaluationCapabilityException()));

        Assert.True(JevResilienceOptions.IsTransient(new EvaluationRateLimitException()));
        Assert.True(JevResilienceOptions.IsTransient(new EvaluationTimeoutException()));
        Assert.True(JevResilienceOptions.IsTransient(new EvaluationProviderException { StatusCode = 503 }));
        Assert.True(JevResilienceOptions.IsTransient(new EvaluationProviderException { StatusCode = 429 }));
        Assert.True(JevResilienceOptions.IsTransient(new EvaluationProviderException { StatusCode = 408 }));

        // A validation error is deterministic: retrying it only spends latency.
        Assert.False(JevResilienceOptions.IsTransient(new EvaluationProviderException { StatusCode = 400 }));
    }

    private sealed class FlakyProvider(int failuresBeforeSuccess, Func<Exception>? failure = null) : IJevProvider
    {
        private int _failures;

        public int CallCount { get; private set; }

        public string Name => "flaky";

        public JevProviderCapabilities Capabilities =>
            JevProviderCapabilities.Noul
            | JevProviderCapabilities.Choice
            | JevProviderCapabilities.Score
            | JevProviderCapabilities.Probabilities
            | JevProviderCapabilities.MultiQuestion
            | JevProviderCapabilities.StructuredState;

        public ValueTask<JevProviderResponse> EvaluateAsync(
            JevProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (_failures < failuresBeforeSuccess)
            {
                _failures++;

                throw failure?.Invoke()
                      ?? new EvaluationProviderException("transient") { Provider = Name, StatusCode = 503 };
            }

            var question = request.Questions[0];

            return new ValueTask<JevProviderResponse>(new JevProviderResponse
            {
                Results =
                [
                    new ChoiceQuestionResult(
                        question.Id,
                        "billing",
                        0.9,
                        new Dictionary<string, double> { ["billing"] = 0.9, ["technical"] = 0.1 }),
                ],
                Metadata = new JevProviderMetadata { Provider = Name },
            });
        }
    }
}
