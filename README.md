# JevGen

[![NuGet](https://img.shields.io/nuget/vpre/JevGen.svg?logo=nuget&label=nuget)](https://www.nuget.org/packages/JevGen)
[![CI](https://github.com/KhaiStimpson/JevGen/actions/workflows/ci.yml/badge.svg)](https://github.com/KhaiStimpson/JevGen/actions/workflows/ci.yml)

**Refit for typed AI decisions.**

JevGen turns an ordinary C# interface into a strongly typed AI decision client at compile time.
No HTTP code, no JSON, no question dictionaries, no result parsing, no runtime reflection.

```csharp
public enum Department
{
    [JevOption("billing", "Invoices, payments and refunds")]
    Billing,

    [JevOption("technical", "Defects, outages and technical support")]
    Technical,

    [JevOption("sales", "Pricing and new business")]
    Sales,
}

[JevClient]
public interface ITicketAI
{
    [JevChoice("Which department should handle this ticket?")]
    Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
}
```

```csharp
builder.Services.AddJevClient<ITicketAI>();
```

```csharp
var result = await ticketAI.RouteAsync(ticket);

if (result.Confidence >= 0.90)
{
    RouteTo(result.Value);
}
```

That is the whole integration. The source generator writes the implementation, the request
construction, the question metadata and the result mapping.

---

## Why the result types look like that

`RouteAsync` returns `ChoiceResult<Department>`, not `Department`. That is the central design
decision in JevGen, and it is deliberate.

A model that answers "Billing" at 94% confidence and one that answers "Billing" at 41% confidence
with most of the remaining probability on "Technical" are telling you very different things. An
API that returns `Department` throws that away and leaves the caller unable to tell the two
apart.

So JevGen preserves it:

```csharp
var result = await ticketAI.RouteAsync(ticket);

result.Value                                // Department.Billing
result.Confidence                           // 0.94
result.ProbabilityOf(Department.Technical)  // 0.04
```

You can still opt into a bare primitive where the uncertainty genuinely has no use, but you have
to say so explicitly with `AllowPrimitiveResult`.

---

## Installation

```bash
dotnet add package JevGen --prerelease
dotnet add package JevGen.Providers.TypeSafe --prerelease   # or JevGen.Providers.OpenRouter
```

`--prerelease` is required: the current release is `1.0.0-preview.2`, and NuGet ignores
prerelease versions unless you ask for them. Pin the version instead if you prefer:
`--version 1.0.0-preview.2`.

The source generator and analyzers flow with `JevGen` as analyzer assets; there is nothing else
to install.

```csharp
builder.Services.AddTypeSafeJev(options => options.ApiKey = configuration["TypeSafe:ApiKey"]);
builder.Services.AddJevClient<ITicketAI>();
```

Or through OpenRouter, which needs no early access:

```csharp
builder.Services.AddOpenRouterJev(options =>
{
    options.ApiKey = configuration["OpenRouter:ApiKey"];
    options.Model = JevModel.Latest;                    // ~typesafe/jev-latest
    // options.Model = "typesafe/jev-1.13-20260917";    // or pin a build
});

builder.Services.AddJevClient<ITicketAI>().UseOpenRouter();
```

Jev is a decisions model, so it is served on its own endpoint rather than through
chat/completions — the provider handles that. See
[provider configuration](docs/provider-configuration.md#endpoints-and-models) for the endpoints
each host uses and the identifiers `JevModel.Latest` resolves to.

---

## What you get

| | |
|---|---|
| **Compile-time contracts** | Twenty diagnostics catch malformed contracts before you run them |
| **One state, many questions** | `[JevEvaluate]` issues every question in a single request |
| **Provider independence** | The same contract runs on TypeSafe, OpenRouter, a gateway or your own provider |
| **Native AOT** | No reflection, no dynamic code; the AOT test publishes and executes a native binary in CI |
| **Testing** | Fakes drive the *real* generated client, so tests cover request and result mapping |
| **Policies** | Confidence thresholds classify results; they never execute business side effects |
| **Observability** | OpenTelemetry activities and metrics, with state recording off by default |
| **Resilience** | Retry, timeout, circuit breaking and cross-provider fallback |

---

## Aggregate evaluations

Several questions against one state, in one round trip:

```csharp
public sealed record TicketAssessment
{
    [JevNoul("Does this require urgent attention?")]
    public required NoulResult Urgent { get; init; }

    [JevChoice("Which department should handle it?")]
    public required ChoiceResult<Department> Department { get; init; }

    [JevScore("Rate the severity.", Min = 1, Max = 5)]
    public required ScoreResult Severity { get; init; }
}

[JevEvaluate]
Task<TicketAssessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

---

## Hosting is separate from semantics

The contract above never names a provider. Switching where it runs is configuration:

```csharp
services.AddJevClient<ITicketAI>().UseTypeSafe();
services.AddJevClient<ITicketAI>().UseOpenRouter();
services.AddJevClient<ITicketAI>().UseProvider<InternalGatewayJevProvider>();
```

Writing your own provider means implementing one interface. It requires no generator changes, no
new attributes, no reflection and no change to any contract. See
[docs/provider-configuration.md](docs/provider-configuration.md).

---

## Testing

```csharp
var fake = JevFake.Create<ITicketAI>();
fake.Returns("route", Department.Billing, confidence: 0.94);

var result = await fake.Client.RouteAsync(ticket);
```

`fake.Client` is the generated implementation, not a mock of the interface. Your test exercises
the real request construction and the real option mapping.

---

## Documentation

- [Getting started](docs/getting-started.md)
- [Defining clients](docs/defining-clients.md)
- [Noul questions](docs/noul-questions.md) · [Choice questions](docs/choice-questions.md) · [Score questions](docs/score-questions.md)
- [Aggregate evaluations](docs/aggregate-evaluations.md)
- [Dependency injection](docs/dependency-injection.md)
- [Provider configuration](docs/provider-configuration.md)
- [Testing](docs/testing.md)
- [Policies](docs/policies.md)
- [Telemetry](docs/telemetry.md)
- [Resilience](docs/resilience.md)
- [ASP.NET Core](docs/aspnet-core.md)
- [Agent routing](docs/agent-routing.md)
- [Native AOT](docs/native-aot.md)
- [Compiler diagnostics](docs/diagnostics.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Architecture](docs/architecture.md)

---

## Samples

| Sample | Shows |
|---|---|
| [TicketRouting](samples/TicketRouting) | The basics, end to end |
| [FraudDetection](samples/FraudDetection) | Why the distribution matters, and decision policies |
| [ContentModeration](samples/ContentModeration) | Asymmetric thresholds for asymmetric costs |
| [AgentToolRouting](samples/AgentToolRouting) | Guardrails, tool routing and model routing |
| [AspNetCore](samples/AspNetCore) | Minimal APIs, health checks and problem details |
| [NativeAot](samples/NativeAot) | Publishing a native binary |

---

## Building

```bash
dotnet build
dotnet test
dotnet publish tests/JevGen.AotTests -c Release -r linux-x64
./tests/JevGen.AotTests/bin/Release/net10.0/linux-x64/publish/JevGen.AotTests
```

Requires the .NET 10 SDK.

---

## Status

Pre-1.0, published on nuget.org as `1.0.0-preview.2`. The public API may still change; see
[docs/architecture.md](docs/architecture.md#breaking-change-strategy) for the versioning policy.

`1.0.0-preview.1` could not reach any live host: it spoke a wire format the System One schema
does not define, and every OpenRouter call failed with `404: Not Found`. `1.0.0-preview.2`
corrects the schema and the endpoints. **Upgrade; preview.1 does not work.**

`JevModel.Fast` and `JevModel.Pro` are gone with it. They named latency and quality tiers no
host publishes, so a contract that used one could only ever fail at run time; removing them
makes that a compile error instead. `JevModel.Latest` remains, and any other identifier is sent
to the host unchanged, which is how a build is pinned.

## Licence

MIT.
