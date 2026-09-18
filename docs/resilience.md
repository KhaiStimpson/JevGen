# Resilience

```csharp
builder.Services.AddJevGen().AddResilience(options =>
{
    options.Timeout = TimeSpan.FromSeconds(3);
    options.MaxRetries = 2;
});
```

Resilience lives in the runtime pipeline rather than in generated clients. That means it can be
added, tuned or removed without regenerating anything, and it applies uniformly to every
provider — including custom ones that do not use an `HttpClient` and so could not be covered by
HTTP-level handlers.

## Options

```csharp
options.Timeout = TimeSpan.FromSeconds(10);              // per attempt
options.MaxRetries = 2;
options.BaseDelay = TimeSpan.FromMilliseconds(200);
options.MaxDelay = TimeSpan.FromSeconds(5);
options.UseJitter = true;
options.CircuitBreakerThreshold = 5;                     // 0 disables
options.CircuitBreakerDuration = TimeSpan.FromSeconds(30);
```

Defaults are conservative. An evaluation is usually billable and often on a user-facing path, so
retrying aggressively costs money and latency without improving the answer.

`Timeout` here is per attempt. `JevGenOptions.Timeout` bounds the whole evaluation including
every retry and fallback; keep the outer budget larger than the inner one times the attempt
count, or the outer one will cut retries short.

## What is retried

| Failure | Retried | Why |
|---|---|---|
| `EvaluationRateLimitException` | Yes | Transient by definition |
| `EvaluationTimeoutException` | Yes | Often transient |
| 5xx, 408, 429 | Yes | Server-side or throttling |
| `HttpRequestException` | Yes | Connection-level |
| `EvaluationAuthenticationException` | **No** | A rejected credential is rejected every time |
| `EvaluationResponseException` | **No** | A malformed response is deterministic |
| `EvaluationSerializationException` | **No** | A serialization bug does not fix itself |
| `EvaluationCapabilityException` | **No** | The provider still will not support it |
| 400, 422 | **No** | The request is invalid as sent |

Retrying a deterministic failure only spends the caller's latency budget before returning the
same error. Override the classification if your provider needs it:

```csharp
options.ShouldRetry = exception =>
    JevResilienceOptions.IsTransient(exception)
    || exception is MyGatewayBusyException;
```

## Backoff

Exponential from `BaseDelay`, capped at `MaxDelay`, with full jitter by default so concurrent
callers recovering from one outage do not retry in lockstep and re-create it.

A provider that reports `Retry-After` wins over the curve: it knows better than any heuristic.

## Circuit breaker

After `CircuitBreakerThreshold` consecutive failures, a provider's circuit opens for
`CircuitBreakerDuration` and further calls fail fast. One request is then let through to probe
recovery; success closes the circuit.

Breaking per provider rather than globally means one failing host does not stop a healthy
fallback from being used.

## Resilience and fallback together

```csharp
builder.Services.AddJevGen().AddResilience(options => options.MaxRetries = 1);

builder.Services.AddJevClient<ITicketAI>()
    .UseTypeSafe()
    .FallbackToOpenRouter();
```

Retries happen within a provider; fallback moves between them. In the worst case above: two
attempts on TypeSafe, then two on OpenRouter. Set `JevGenOptions.Timeout` so that whole sequence
still fits inside what your caller will wait.

## Using HTTP-level resilience instead

If you prefer resilience at the transport, configure
`Microsoft.Extensions.Http.Resilience` on each provider's client and leave `AddResilience` off:

```csharp
builder.Services.AddHttpClient<TypeSafeJevProvider>().AddStandardResilienceHandler();
```

Applying both means retries multiply. Pick one.
