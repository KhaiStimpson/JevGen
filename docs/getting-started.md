# Getting started

## Install

```bash
dotnet add package JevGen --prerelease
dotnet add package JevGen.Providers.TypeSafe --prerelease
```

`JevGen` brings the source generator and analyzers with it as analyzer assets. You do not
reference them separately.

`--prerelease` is required while the current release is `1.0.0-preview.2`; NuGet skips prerelease
versions otherwise. Use `--version 1.0.0-preview.2` to pin instead.

Requires .NET 10 or later and C# 11 or later.

## Declare a contract

```csharp
using JevGen;

public enum Department
{
    [JevOption("billing", "Invoices, payments, subscriptions and refunds")]
    Billing,

    [JevOption("technical", "Software defects, outages and technical support")]
    Technical,

    [JevOption("sales", "Pricing questions, upgrades and new business")]
    Sales,
}

public sealed record Ticket
{
    public required string Subject { get; init; }
    public required string Body { get; init; }
}

[JevClient]
public interface ITicketAI
{
    [JevChoice("Which department should handle this ticket?")]
    Task<ChoiceResult<Department>> RouteAsync(
        Ticket ticket,
        CancellationToken cancellationToken = default);
}
```

The criteria on each enum member are not decoration. They are what the model is told about when
each option applies, and leaving them out measurably degrades routing quality — which is why
JevGen warns about it ([JEV007](diagnostics.md#jev007)).

## Declare a serializer context

```csharp
using System.Text.Json.Serialization;

[JsonSerializable(typeof(Ticket))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

[assembly: JevJsonContext(typeof(AppJsonContext))]
```

This keeps state serialization reflection-free. It is required for trimming and Native AOT, and
a good idea everywhere else. Skipping it is legal; JevGen falls back to reflection and tells you
so with [JEV020](diagnostics.md#jev020).

## Register

```csharp
builder.Services.AddTypeSafeJev(options =>
{
    options.ApiKey = builder.Configuration["TypeSafe:ApiKey"];
});

builder.Services.AddJevClient<ITicketAI>();
```

## Use it

```csharp
public sealed class TicketService(ITicketAI ai)
{
    public async Task HandleAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var result = await ai.RouteAsync(ticket, cancellationToken);

        if (result.Confidence >= 0.90)
        {
            RouteTo(result.Value);
            return;
        }

        QueueForHumanReview(ticket, result);
    }
}
```

`RouteAsync` returns `ChoiceResult<Department>`, so the confidence and the full distribution are
available to decide with. That is the point; see
[Why the result types look like that](../README.md#why-the-result-types-look-like-that).

## Inspect what it will send

Before spending a single token:

```csharp
Console.WriteLine(JevGenDebug.Describe<ITicketAI>());
```

```json
{
  "client": "ITicketAI",
  "methods": [
    {
      "method": "RouteAsync",
      "state": "Support.Ticket",
      "questions": [
        { "id": "route", "type": "choice", "prompt": "Which department should handle this ticket?" }
      ]
    }
  ]
}
```

Or from the command line, over a built assembly:

```bash
dotnet tool install -g JevGen.Cli --prerelease
jevgen inspect ./bin/Debug/net10.0/MyApp.dll
```

```
ITicketAI.RouteAsync
  State: Support.Ticket
  Questions:
    route  Choice<billing | technical | sales>
```

Neither needs credentials or a network.

## Write a test

```csharp
[Fact]
public async Task BillingTicketsRouteToBilling()
{
    var fake = JevFake.Create<ITicketAI>();
    fake.Returns("route", Department.Billing, confidence: 0.94);

    var result = await fake.Client.RouteAsync(new Ticket { Subject = "Refund", Body = "..." });

    Assert.Equal(Department.Billing, result.Value);
}
```

`fake.Client` is the generated client. The test runs the real request construction and the real
option mapping, with a scripted answer in place of the provider.

## Next

- [Defining clients](defining-clients.md) — the full contract surface
- [Aggregate evaluations](aggregate-evaluations.md) — several questions, one request
- [Provider configuration](provider-configuration.md) — hosting, fallback, custom providers
- [Testing](testing.md)
