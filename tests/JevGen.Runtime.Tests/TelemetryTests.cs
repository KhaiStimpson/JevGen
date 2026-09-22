using System.Diagnostics.Metrics;
using JevGen.Providers;
using JevGen.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JevGen.Runtime.Tests;

/// <summary>Retries and fallbacks are different events and are counted separately.</summary>
public sealed class TelemetryTests
{
    private static Ticket SampleTicket => new() { Subject = "Refund" };

    [Fact]
    public async Task RetriesAndFallbacksAreCountedSeparately()
    {
        // A unique contract tag keeps measurements from tests running in parallel apart.
        var primary = new FakeProvider("telemetry-primary")
        {
            Failure = new EvaluationProviderException("down") { StatusCode = 503 },
        };

        var secondary = new FakeProvider("telemetry-secondary");

        var services = new ServiceCollection();

        services.AddJevGen(options => options.ValidateOnStart = false)
            .AddJevGenTelemetry()
            .AddResilience(options =>
            {
                options.MaxRetries = 2;
                options.BaseDelay = TimeSpan.FromMilliseconds(1);
                options.UseJitter = false;
                options.CircuitBreakerThreshold = 0;
            });

        // Registering twice must not double the measurements.
        services.AddJevGenTelemetry();

        services.AddSingleton<IJevProvider>(primary);
        services.AddSingleton<IJevProvider>(secondary);
        services.AddJevClient<INamedTicketAI>().UseProvider("telemetry-primary").FallbackTo("telemetry-secondary");

        await using var provider = services.BuildServiceProvider();

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == JevGenTelemetry.Name)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "jevgen.contract" && Equals(tag.Value, "TicketRouter"))
                {
                    lock (counts)
                    {
                        counts[instrument.Name] = counts.GetValueOrDefault(instrument.Name) + value;
                    }
                }
            }
        });

        listener.Start();

        await provider.GetRequiredService<INamedTicketAI>().RouteAsync(SampleTicket);

        // Three attempts on the primary (two of them retries), then one fallback.
        Assert.Equal(3, primary.CallCount);
        Assert.Equal(2, counts.GetValueOrDefault("jevgen.retries"));
        Assert.Equal(1, counts.GetValueOrDefault("jevgen.fallbacks"));
        Assert.Equal(2, counts.GetValueOrDefault("jevgen.requests"));
    }
}
