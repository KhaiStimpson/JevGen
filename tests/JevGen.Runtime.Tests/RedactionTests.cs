using JevGen.Providers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JevGen.Runtime.Tests;

/// <summary>
/// Sensitive state reaches the provider — it is part of what the model reasons about — but must
/// never reach logs, traces or debug output.
/// </summary>
public sealed class RedactionTests
{
    private static Ticket SampleTicket => new()
    {
        Subject = "Billed twice",
        Body = "Please refund one charge.",
        CustomerEmail = "customer@example.com",
    };

    [Fact]
    public async Task SensitiveStateIsSentToTheProvider()
    {
        var provider = new FakeProvider("primary");

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);
        services.AddSingleton<IJevProvider>(provider);
        services.AddJevClient<ITicketAI>();

        await using var built = services.BuildServiceProvider();
        await built.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        // The model needs it, so it goes on the wire.
        var state = provider.LastRequest!.SerializeState(JevGenJson.Options);
        Assert.Equal("customer@example.com", state.GetProperty("customerEmail").GetString());
    }

    [Fact]
    public async Task SensitiveStateIsRedactedFromDiagnostics()
    {
        // A filter observes exactly what instrumentation observes.
        var capture = new CapturingFilter();

        var services = new ServiceCollection();
        services.AddJevGen(options => options.ValidateOnStart = false);
        services.AddSingleton<IJevProvider>(new FakeProvider("primary"));
        services.AddSingleton<IEvaluationFilter>(capture);
        services.AddJevClient<ITicketAI>();

        await using var built = services.BuildServiceProvider();
        await built.GetRequiredService<ITicketAI>().RouteAsync(SampleTicket);

        // The generator captured the sensitive property at compile time.
        Assert.Equal(["CustomerEmail"], capture.Request!.SensitiveProperties.AsEnumerable());

        var described = JevRedaction.Describe(capture.Request);

        Assert.DoesNotContain("customer@example.com", described, StringComparison.Ordinal);
        Assert.Contains(JevRedaction.Placeholder, described, StringComparison.Ordinal);

        // Everything else is still there: redaction is targeted, not a blanket refusal to log.
        Assert.Contains("Billed twice", described, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactionCoversNestedObjectsAndCollections()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""
            {
              "subject": "Outage",
              "customerEmail": "a@example.com",
              "related": [ { "customerEmail": "b@example.com", "subject": "Also down" } ]
            }
            """);

        var redacted = JevRedaction.Redact(document.RootElement, ["CustomerEmail"]);

        Assert.DoesNotContain("a@example.com", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("b@example.com", redacted, StringComparison.Ordinal);
        Assert.Contains("Outage", redacted, StringComparison.Ordinal);
        Assert.Contains("Also down", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribingUnserializableStateNeverThrows()
    {
        // Diagnostics must not be the thing that fails an evaluation.
        var request = new EvaluationRequest
        {
            State = new object(),
            Questions = [],
            ClientName = "X",
            MethodName = "Y",
        };

        Assert.Equal("Object", JevRedaction.Describe(request));
    }

    /// <summary>Captures the canonical request, the way instrumentation sees it.</summary>
    private sealed class CapturingFilter : IEvaluationFilter
    {
        internal EvaluationRequest? Request { get; private set; }

        public ValueTask<EvaluationResponse> InvokeAsync(
            EvaluationContext context,
            EvaluationDelegate next,
            CancellationToken cancellationToken = default)
        {
            Request = context.Request;
            return next(context, cancellationToken);
        }
    }
}
