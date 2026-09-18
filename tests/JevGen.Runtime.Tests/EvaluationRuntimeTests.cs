using JevGen.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JevGen.Runtime.Tests;

/// <summary>Covers the runtime pipeline: selection, mapping, capabilities and fallback.</summary>
public sealed class EvaluationRuntimeTests
{
    private static ServiceProvider Build(
        Action<IServiceCollection> configure,
        Action<JevGenOptions>? options = null)
    {
        var services = new ServiceCollection();
        services.AddJevGen(configured =>
        {
            configured.ValidateOnStart = false;
            options?.Invoke(configured);
        });

        configure(services);
        return services.BuildServiceProvider();
    }

    private static Ticket SampleTicket => new() { Subject = "Refund for a duplicate charge", Body = "I was billed twice." };

    [Fact]
    public async Task ChoiceResultCarriesValueConfidenceAndDistribution()
    {
        var provider = new FakeProvider("primary") { SelectedOption = "technical", Confidence = 0.88d };

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(provider);
            s.AddJevClient<ITicketAI>();
        });

        var result = await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Technical, result.Value);
        Assert.Equal(0.88d, result.Confidence, 6);
        Assert.Equal(3, result.Probabilities.Count);
        Assert.Equal(0.88d, result.ProbabilityOf(Department.Technical), 6);

        // Confidence and the distribution survive the round trip: nothing collapses to a primitive.
        Assert.True(result.Probabilities.ContainsKey(Department.Billing));
        Assert.True(result.Probabilities.ContainsKey(Department.Sales));
    }

    [Fact]
    public async Task NoulPreservesProbabilityRatherThanCollapsingToBoolean()
    {
        var provider = new FakeProvider("primary") { Confidence = 0.31d };

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(provider);
            s.AddJevClient<ITicketAI>();
        });

        var result = await services.GetRequiredService<ITicketAI>().IsUrgentAsync(SampleTicket);

        Assert.Equal(0.31d, result.Probability, 6);
        Assert.False(result.Value());
        Assert.True(result.Value(threshold: 0.3d));
    }

    [Fact]
    public async Task AggregateIssuesEveryQuestionInASingleProviderCall()
    {
        var provider = new FakeProvider("primary");

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(provider);
            s.AddJevClient<ITicketAI>();
        });

        var assessment = await services.GetRequiredService<ITicketAI>().AssessAsync(SampleTicket);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(3, provider.LastRequest!.Questions.Length);
        Assert.NotEqual(default, assessment.Urgent);
        Assert.Equal(Department.Billing, assessment.Department.Value);
        Assert.Equal(3d, assessment.Severity.Value, 6);
    }

    [Fact]
    public async Task RequestsCarryContractMetadata()
    {
        var provider = new FakeProvider("primary");

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(provider);
            s.AddJevClient<ITicketAI>();
        });

        await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        var request = provider.LastRequest!;
        Assert.Equal("ITicketAI", request.ClientName);
        Assert.Equal("RouteAsync", request.MethodName);
        Assert.Same(SampleTicket.GetType(), request.State.GetType());
        Assert.NotNull(request.StateTypeInfo);
    }

    [Fact]
    public async Task DecisionPolicyClassifiesWithoutActing()
    {
        var provider = new FakeProvider("primary") { Confidence = 0.75d };

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(provider);
            s.AddJevClient<ITicketAI>();
        });

        var decision = await services.GetRequiredService<ITicketAI>().DecideAsync(SampleTicket);

        Assert.Equal(DecisionAction.Review, decision.Action);
        Assert.True(decision.NeedsReview);
        Assert.Equal(Department.Billing, decision.Value);
    }

    [Fact]
    public async Task ProviderSelectionPrefersTheConfiguredClientProvider()
    {
        var primary = new FakeProvider("primary");
        var secondary = new FakeProvider("secondary");

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(primary);
            s.AddSingleton<IJevProvider>(secondary);
            s.AddJevClient<ITicketAI>().UseProvider("secondary");
        });

        await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(0, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
    }

    [Fact]
    public async Task DefaultProviderIsUsedWhenNothingOverridesSelection()
    {
        var primary = new FakeProvider("primary");
        var secondary = new FakeProvider("secondary");

        await using var services = Build(
            s =>
            {
                s.AddSingleton<IJevProvider>(primary);
                s.AddSingleton<IJevProvider>(secondary);
                s.AddJevClient<ITicketAI>();
            },
            options => options.DefaultProvider = "secondary");

        await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(1, secondary.CallCount);
    }

    [Fact]
    public async Task MissingCapabilityFailsRatherThanDegrading()
    {
        // The contract returns ChoiceResult<T>, which exposes a distribution. A provider that
        // cannot produce one must be refused, not quietly allowed to answer without it.
        var limited = new FakeProvider(
            "limited",
            JevProviderCapabilities.Choice | JevProviderCapabilities.StructuredState);

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(limited);
            s.AddJevClient<ITicketAI>();
        });

        var exception = await Assert.ThrowsAsync<EvaluationCapabilityException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Equal("limited", exception.Provider);
        Assert.True(exception.Missing.HasFlag(JevCapabilitySet.Probabilities));
        Assert.Equal(0, limited.CallCount);
    }

    [Fact]
    public async Task DegradedCapabilitiesRequireAnExplicitOptIn()
    {
        var limited = new FakeProvider(
            "limited",
            JevProviderCapabilities.Choice | JevProviderCapabilities.StructuredState);

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(limited);
            s.AddJevClient<ITicketAI>().AllowDegradedCapabilities();
        });

        var result = await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(1, limited.CallCount);
        Assert.Equal(Department.Billing, result.Value);
    }

    [Fact]
    public async Task FallbackRunsOnATransientFailure()
    {
        var primary = new FakeProvider("primary")
        {
            Failure = new EvaluationProviderException("upstream is down") { Provider = "primary", StatusCode = 503 },
        };

        var secondary = new FakeProvider("secondary") { SelectedOption = "sales" };

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(primary);
            s.AddSingleton<IJevProvider>(secondary);
            s.AddJevClient<ITicketAI>().UseProvider("primary").FallbackTo("secondary");
        });

        var result = await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Sales, result.Value);
        Assert.Equal("secondary", result.Metadata!.Provider);
        Assert.Equal(2, result.Metadata.Attempts);
    }

    [Fact]
    public async Task FallbackNeverRunsOnAnAuthenticationFailure()
    {
        // A rejected credential fails identically everywhere. Failing over would spend the
        // caller's latency budget and possibly a second provider's quota for nothing.
        var primary = new FakeProvider("primary")
        {
            Failure = new EvaluationAuthenticationException("bad key") { Provider = "primary" },
        };

        var secondary = new FakeProvider("secondary");

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(primary);
            s.AddSingleton<IJevProvider>(secondary);
            s.AddJevClient<ITicketAI>().UseProvider("primary").FallbackTo("secondary");
        });

        await Assert.ThrowsAsync<EvaluationAuthenticationException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Equal(0, secondary.CallCount);
    }

    [Fact]
    public async Task FallbackRunsWhenAProviderLacksARequiredCapability()
    {
        var limited = new FakeProvider(
            "limited",
            JevProviderCapabilities.Choice | JevProviderCapabilities.StructuredState);

        var capable = new FakeProvider("capable") { SelectedOption = "technical" };

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(limited);
            s.AddSingleton<IJevProvider>(capable);
            s.AddJevClient<ITicketAI>().UseProvider("limited").FallbackTo("capable");
        });

        var result = await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(Department.Technical, result.Value);
        Assert.Equal(0, limited.CallCount);
    }

    [Fact]
    public async Task ConfidenceFallbackReturnsTheBestAnswerRatherThanFailing()
    {
        var primary = new FakeProvider("primary") { Confidence = 0.30d, SelectedOption = "billing" };
        var secondary = new FakeProvider("secondary") { Confidence = 0.45d, SelectedOption = "sales" };

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(primary);
            s.AddSingleton<IJevProvider>(secondary);
            s.AddJevClient<ITicketAI>()
                .UseProvider("primary")
                .FallbackTo("secondary")
                .FallbackWhenConfidenceBelow(0.6d);
        });

        var result = await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);

        // Both were below the floor, so the better of the two is returned and the caller's
        // policy decides what to do with a low-confidence answer.
        Assert.Equal(Department.Sales, result.Value);
        Assert.Equal(0.45d, result.Confidence, 6);
    }

    [Fact]
    public async Task FallbackExhaustionReportsEveryProviderTried()
    {
        var primary = new FakeProvider("primary")
        {
            Failure = new EvaluationProviderException("down") { Provider = "primary", StatusCode = 502 },
        };

        var secondary = new FakeProvider("secondary")
        {
            Failure = new EvaluationProviderException("also down") { Provider = "secondary", StatusCode = 502 },
        };

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(primary);
            s.AddSingleton<IJevProvider>(secondary);
            s.AddJevClient<ITicketAI>().UseProvider("primary").FallbackTo("secondary");
        });

        var exception = await Assert.ThrowsAsync<EvaluationFallbackExhaustedException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Equal(["primary", "secondary"], exception.AttemptedProviders);
    }

    [Fact]
    public async Task AMissingAnswerIsReportedRatherThanSilentlyDefaulted()
    {
        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(new SilentProvider());
            s.AddJevClient<ITicketAI>();
        });

        await Assert.ThrowsAsync<EvaluationResponseException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));
    }

    [Fact]
    public async Task CancellationPropagatesToTheProvider()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(new CancellationAwareProvider());
            s.AddJevClient<ITicketAI>();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket, source.Token));
    }

    [Fact]
    public async Task EvaluationTimesOutAgainstTheConfiguredBudget()
    {
        await using var services = Build(
            s =>
            {
                s.AddSingleton<IJevProvider>(new SlowProvider());
                s.AddJevClient<ITicketAI>();
            },
            options => options.Timeout = TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<EvaluationTimeoutException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));
    }

    [Fact]
    public async Task RegisteringAContractWithNoProviderIsDiagnosable()
    {
        await using var services = Build(s => s.AddJevClient<ITicketAI>());

        var exception = await Assert.ThrowsAsync<JevGenException>(
            () => services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket));

        Assert.Contains("No provider is registered", exception.Message, StringComparison.Ordinal);
    }

    private sealed class SilentProvider : IJevProvider
    {
        public string Name => "silent";

        public JevProviderCapabilities Capabilities => JevProviderCapabilities.Choice
            | JevProviderCapabilities.Probabilities
            | JevProviderCapabilities.StructuredState;

        public ValueTask<JevProviderResponse> EvaluateAsync(
            JevProviderRequest request,
            CancellationToken cancellationToken = default)
            => new(new JevProviderResponse
            {
                Results = [],
                Metadata = new JevProviderMetadata { Provider = Name },
            });
    }

    private sealed class CancellationAwareProvider : IJevProvider
    {
        public string Name => "cancellable";

        public JevProviderCapabilities Capabilities => JevProviderCapabilities.Choice
            | JevProviderCapabilities.Probabilities
            | JevProviderCapabilities.StructuredState;

        public ValueTask<JevProviderResponse> EvaluateAsync(
            JevProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The provider should not have been reached.");
        }
    }

    private sealed class SlowProvider : IJevProvider
    {
        public string Name => "slow";

        public JevProviderCapabilities Capabilities => JevProviderCapabilities.Choice
            | JevProviderCapabilities.Probabilities
            | JevProviderCapabilities.StructuredState;

        public async ValueTask<JevProviderResponse> EvaluateAsync(
            JevProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }
    }
}
