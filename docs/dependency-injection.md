# Dependency injection

## The minimum

```csharp
builder.Services.AddJevClient<ITicketAI>();
```

`AddJevClient<T>` resolves the implementation from a registry the source generator populates
from a module initializer. There is no runtime proxy, no assembly scanning and no reflection
over attributes, which is what keeps the whole path trim- and AOT-safe.

If no client was generated for the contract, the call throws immediately with an explanation,
rather than failing later at the first evaluation.

## Configuring the runtime

```csharp
builder.Services.AddJevGen(options =>
{
    options.DefaultModel = "jev-latest";
    options.DefaultProvider = "typesafe";
    options.Timeout = TimeSpan.FromSeconds(30);
    options.RecordMetadata = true;
    options.CapabilityValidation = CapabilityValidationMode.Fail;
    options.ValidateOnStart = true;
});
```

Or bind from configuration:

```csharp
builder.Services.AddJevGen(builder.Configuration.GetSection("JevGen"));
```

`Timeout` is the budget for the whole evaluation, including retries and fallbacks. Per-attempt
timeouts belong to [resilience](resilience.md).

## Providers

```csharp
builder.Services.AddTypeSafeJev(options => options.ApiKey = configuration["TypeSafe:ApiKey"]);
builder.Services.AddOpenRouterJev(options => options.ApiKey = configuration["OpenRouter:ApiKey"]);
```

Custom providers, named registration and per-client selection are covered in
[provider-configuration.md](provider-configuration.md).

## Per-contract configuration

```csharp
builder.Services.AddJevClient<ITicketAI>()
    .UseTypeSafe()
    .FallbackToOpenRouter()
    .FallbackWhenConfidenceBelow(0.60)
    .UseModel("jev-pro")
    .ConfigureProvider("openrouter", options => options["providerOrder"] = "typesafe");
```

Values set here are explicit programmatic configuration and take precedence over the equivalent
attributes on the contract, so operational decisions can change without editing contract code.

## Registering everything

```csharp
builder.Services.AddAllJevClients();
```

This reads the generated registry, not the assembly. Nothing is discovered by scanning, so it
cannot pick up a contract the compiler did not see and it stays AOT-safe. There is deliberately
no `AddAllJevClients(Assembly)` overload.

## Combined

```csharp
builder.Services
    .AddJevGen(options => options.DefaultModel = "jev-latest")
    .AddResilience(resilience => resilience.MaxRetries = 2)
    .AddJevGenTelemetry();

builder.Services.AddTypeSafeJev(options => options.ApiKey = configuration["TypeSafe:ApiKey"]);
builder.Services.AddJevClient<ITicketAI>().UseTypeSafe();
```

## Start-up validation

With `ValidateOnStart` (the default), a hosted service checks every registered contract against
the provider that will run it as the application starts. A contract requiring semantics its
provider cannot honour fails the deployment instead of the first request.

Turn it off in tests that register no provider:

```csharp
services.AddJevGen(options => options.ValidateOnStart = false);
```

## Lifetimes

| Service | Lifetime | Why |
|---|---|---|
| Generated clients | Transient | Cheap: a single field holding the runtime |
| `IEvaluationRuntime` | Singleton | Holds the filter pipeline |
| `IJevProviderResolver` | Singleton | An index over registered providers |
| Providers | Singleton | Hold a pooled `HttpClient` |
| Filters | Singleton | Instrumentation and resilience state |

## Custom filters

Cross-cutting behaviour hooks in without touching generated code:

```csharp
public sealed class AuditFilter(IDecisionAuditSink sink) : IEvaluationFilter
{
    public int Order => 500;

    public async ValueTask<EvaluationResponse> InvokeAsync(
        EvaluationContext context,
        EvaluationDelegate next,
        CancellationToken cancellationToken = default)
    {
        var response = await next(context, cancellationToken);

        await sink.RecordAsync(new DecisionAudit
        {
            Contract = context.Request.ClientName,
            Method = context.Request.MethodName,
            Provider = response.Metadata.Provider,
            Model = response.Metadata.Model ?? "unknown",
            Timestamp = DateTimeOffset.UtcNow,
            Confidence = 1,
            ContractVersion = context.Request.ContractVersion,
        }, cancellationToken);

        return response;
    }
}
```

```csharp
builder.Services.AddJevGen().AddEvaluationFilter<AuditFilter>();
```

Lower `Order` runs further out. Telemetry uses `int.MinValue`; resilience uses 100.
