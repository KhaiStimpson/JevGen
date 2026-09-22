using JevGen.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace JevGen.Runtime.Tests;

/// <summary>
/// Per-contract configuration must reach the contract it was written for, and start-up
/// validation must check the provider the runtime will actually use.
/// </summary>
public sealed class ClientConfigurationTests
{
    private static Ticket SampleTicket => new() { Subject = "Refund" };

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ConfigurationReachesAContractWithACustomName()
    {
        var primary = new FakeProvider("primary");
        var secondary = new FakeProvider("secondary");

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(primary);
            s.AddSingleton<IJevProvider>(secondary);
            s.AddJevClient<INamedTicketAI>().UseProvider("secondary");
        });

        var result = await services.GetRequiredService<INamedTicketAI>().RouteAsync(SampleTicket);

        Assert.Equal("secondary", result.Metadata!.Provider);
        Assert.Equal(0, primary.CallCount);
    }

    [Fact]
    public async Task ConfigurationDoesNotLeakToAContractThatSharesTheName()
    {
        var primary = new FakeProvider("primary");
        var secondary = new FakeProvider("secondary");

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(primary);
            s.AddSingleton<IJevProvider>(secondary);
            s.AddJevClient<Elsewhere.ITicketAI>().UseProvider("secondary");
            s.AddJevClient<ITicketAI>();
        });

        await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);
        await services.GetRequiredService<Elsewhere.ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, secondary.CallCount);
    }

    [Fact]
    public async Task ConfigurationKeyedByTheBareNameStillApplies()
    {
        var primary = new FakeProvider("primary");
        var secondary = new FakeProvider("secondary");

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(primary);
            s.AddSingleton<IJevProvider>(secondary);
            s.AddJevClient<ITicketAI>();

            // What configuration binding produces from { "Clients": { "ITicketAI": { ... } } }.
            s.Configure<JevGenOptions>(options =>
                options.Clients["ITicketAI"] = new JevClientConfiguration { Provider = "secondary" });
        });

        await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal(1, secondary.CallCount);
    }

    [Fact]
    public async Task UseModelOverridesTheDeclaredModel()
    {
        var provider = new FakeProvider("primary");

        await using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(provider);
            s.AddJevClient<ITicketAI>().UseModel("pinned-model");
        });

        await services.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        Assert.Equal("pinned-model", provider.LastRequest!.Model);
    }

    [Fact]
    public void StartupValidationChecksTheProviderSelectedByType()
    {
        var limited = new NoulOnlyProvider();
        var full = new FakeProvider("full");

        using var services = Build(s =>
        {
            // The limited provider is registered first, so it is the default.
            s.AddSingleton<IJevProvider>(limited);
            s.AddSingleton<IJevProvider>(full);
            s.AddJevClient<ITicketAI>().UseProvider<FakeProvider>();
        });

        var violations = CapabilityValidator.Validate(
            [JevClientRegistry.Get<ITicketAI>()],
            services.GetRequiredService<IJevProviderResolver>(),
            services.GetRequiredService<IOptions<JevGenOptions>>().Value);

        Assert.Empty(violations);
    }

    [Fact]
    public void StartupValidationRequiresModelSelectionForAConfiguredModel()
    {
        var provider = new FakeProvider("primary")
        {
            Capabilities = JevProviderCapabilities.Noul
                | JevProviderCapabilities.Choice
                | JevProviderCapabilities.Score
                | JevProviderCapabilities.Probabilities
                | JevProviderCapabilities.MultiQuestion
                | JevProviderCapabilities.StructuredState,
        };

        using var services = Build(s =>
        {
            s.AddSingleton<IJevProvider>(provider);
            s.AddJevClient<ITicketAI>().UseModel("pinned-model");
        });

        var violations = CapabilityValidator.Validate(
            [JevClientRegistry.Get<ITicketAI>()],
            services.GetRequiredService<IJevProviderResolver>(),
            services.GetRequiredService<IOptions<JevGenOptions>>().Value);

        Assert.NotEmpty(violations);
        Assert.All(violations, violation => Assert.Equal(JevCapabilitySet.ModelSelection, violation.Missing));
    }

    private sealed class NoulOnlyProvider : IJevProvider
    {
        public string Name => "noul-only";

        public JevProviderCapabilities Capabilities => JevProviderCapabilities.Noul | JevProviderCapabilities.StructuredState;

        public ValueTask<JevProviderResponse> EvaluateAsync(
            JevProviderRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The provider should not have been reached.");
    }
}
