# ASP.NET Core

## Endpoints

```csharp
app.MapPost("/tickets/assess", async (Ticket ticket, ITicketAI ai, CancellationToken ct) =>
{
    var result = await ai.AssessAsync(ticket, ct);
    return Results.Ok(result);
});
```

No provider-specific code, no JSON handling, no result parsing. The endpoint depends on the
contract and nothing else.

## Problem details

```csharp
app.MapPost("/tickets/route", Handler)
   .WithJevProblemDetails(app.Environment.IsDevelopment());
```

JevGen failures become RFC 9457 responses, mapped so the status distinguishes an upstream
problem from the caller's:

| Exception | Status |
|---|---|
| `EvaluationRateLimitException` | 429, with `retryAfterSeconds` |
| `EvaluationTimeoutException` | 504 |
| `EvaluationAuthenticationException` | **503** |
| `EvaluationCapabilityException` | 503 |
| `EvaluationFallbackExhaustedException` | 502 |
| `EvaluationResponseException` | 502 |
| `EvaluationProviderException` | 502 |
| `EvaluationSerializationException` | 500 |

An authentication failure is 503, not 401: it means *this service* is misconfigured, not that
the caller's credentials were wrong. Returning 401 would send them chasing their own auth.

Exception messages are only included when you pass `includeDetail`, which should be development
only — messages can name internal endpoints and configuration. The provider name and request id
are always included, because they are what makes an upstream failure traceable and neither is
sensitive.

## Confidence gates

```csharp
app.MapPost("/tickets/route", Handler)
   .RequireConfidence(0.90);
```

A result below the threshold becomes a 422 explaining that the model was not confident enough,
with the actual and required confidence in the body.

The gate **refuses**; it does not substitute a different answer. A caller gets an explicit "not
confident enough" rather than a low-confidence guess presented as certain.

## Health checks

```csharp
builder.Services.AddHealthChecks().AddJev();
app.MapHealthChecks("/health");
```

The check verifies that providers are registered and configured, asks each for its own readiness
signal where it implements `IJevProviderHealth`, and confirms every registered contract is
satisfiable by its provider.

It **never issues an evaluation**. Probes run constantly and an evaluation is billable; a health
check that quietly spends money would be a bad trade.

| Result | Meaning |
|---|---|
| Healthy | Providers are usable and every contract is satisfiable |
| Degraded | A contract requires semantics its provider does not support |
| Unhealthy | No provider is registered, or one reported itself unusable |

## Configuration

```json
{
  "JevGen": {
    "DefaultModel": "jev-latest",
    "Timeout": "00:00:05",
    "CapabilityValidation": "Fail"
  },
  "TypeSafe": {
    "Model": "jev-latest"
  }
}
```

```csharp
builder.Services.AddJevGen(builder.Configuration.GetSection("JevGen"));
builder.Services.AddTypeSafeJev(builder.Configuration.GetSection("TypeSafe"));
```

Keep API keys out of `appsettings.json`; use user secrets, environment variables or Key Vault
through the standard configuration providers.

Start-up validation is on by default, so a contract its provider cannot serve fails the
deployment rather than the first request.

## Native AOT

ASP.NET Core with Native AOT needs the JSON contexts wired explicitly:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default));
```

See [native-aot.md](native-aot.md).

## Full example

[samples/AspNetCore](../samples/AspNetCore) is a working minimal API with routing, aggregate
assessment, a confidence gate, health checks and a contract-inspection endpoint. It runs without
credentials by falling back to the testing package.
