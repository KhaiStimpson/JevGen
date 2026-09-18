using AspNetCoreSample;
using JevGen;
using JevGen.AspNetCore;
using JevGen.Providers.OpenRouter;
using JevGen.Providers.TypeSafe;
using JevGen.Telemetry;
using JevGen.Testing;

[assembly: JevJsonContext(typeof(ApiJsonContext))]

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));

// JevGen registration. The contract in Contracts.cs never mentions a provider, HTTP or JSON,
// and nothing below changes when the hosting does.
builder.Services
    .AddJevGen(options =>
    {
        options.DefaultModel = "jev-latest";
        options.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddResilience(resilience =>
    {
        resilience.Timeout = TimeSpan.FromSeconds(3);
        resilience.MaxRetries = 2;
    })
    .AddJevGenTelemetry(telemetry =>
    {
        telemetry.RecordConfidence = true;
        telemetry.RecordQuestionNames = true;

        // Ticket bodies are customer data. They stay out of telemetry.
        telemetry.RecordState = false;
    });

if (builder.Configuration["TypeSafe:ApiKey"] is { Length: > 0 })
{
    builder.Services.AddTypeSafeJev(options =>
    {
        options.ApiKey = builder.Configuration["TypeSafe:ApiKey"];
    });

    builder.Services.AddOpenRouterJev(options =>
    {
        options.ApiKey = builder.Configuration["OpenRouter:ApiKey"];
        options.SiteName = "JevGen ASP.NET Core sample";
    });

    // Direct by default, falling back to OpenRouter when TypeSafe cannot serve a request.
    builder.Services.AddJevClient<ITicketAI>().UseTypeSafe().FallbackToOpenRouter();
}
else
{
    // No credentials configured, so the sample runs against deterministic answers. This is the
    // testing package doing exactly what it does in a test suite.
    builder.Services.AddJevFake<ITicketAI>(fake => fake
        .Returns(
            "route",
            Department.Billing,
            0.94,
            new Dictionary<Department, double>
            {
                [Department.Billing] = 0.94,
                [Department.Technical] = 0.04,
                [Department.Sales] = 0.02,
            })
        .ReturnsProbability("urgent", 0.17)
        .Returns("department", Department.Billing, 0.94)
        .ReturnsScore("severity", 2, 0.8));
}

// The JevGen health check reports on registered providers. In fake mode there are none by
// design, so it is only added when the sample is actually configured to call one.
if (builder.Configuration["TypeSafe:ApiKey"] is { Length: > 0 })
{
    builder.Services.AddHealthChecks().AddJev();
}
else
{
    builder.Services.AddHealthChecks();
}

var app = builder.Build();

app.MapHealthChecks("/health");

// The endpoint contains no provider-specific code, no JSON handling and no result parsing.
app.MapPost("/tickets/route", async (Ticket ticket, ITicketAI ai, CancellationToken cancellationToken) =>
    {
        var result = await ai.RouteAsync(ticket, cancellationToken);

        return Results.Ok(new RoutingResponse(
            result.Value.ToString(),
            result.Confidence,
            result.Probabilities.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value)));
    })
    .WithJevProblemDetails(app.Environment.IsDevelopment())
    .WithName("RouteTicket");

// The same call, refusing to answer below 90% confidence rather than returning a guess.
app.MapPost("/tickets/route-confident", async (Ticket ticket, ITicketAI ai, CancellationToken cancellationToken) =>
        await ai.RouteAsync(ticket, cancellationToken))
    .RequireConfidence(0.90)
    .WithJevProblemDetails(app.Environment.IsDevelopment())
    .WithName("RouteTicketConfidently");

app.MapPost("/tickets/assess", async (Ticket ticket, ITicketAI ai, CancellationToken cancellationToken) =>
        Results.Ok(await ai.AssessAsync(ticket, cancellationToken)))
    .WithJevProblemDetails(app.Environment.IsDevelopment())
    .WithName("AssessTicket");

// Shows what the contract will send, without credentials or a network call.
app.MapGet("/tickets/contract", () => Results.Text(JevGenDebug.Describe<ITicketAI>(), "application/json"))
    .WithName("DescribeContract");

await app.RunAsync();

/// <summary>Exposed so the integration tests can host this application with WebApplicationFactory.</summary>
public partial class Program;
