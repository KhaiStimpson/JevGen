# Provider configuration

Jev *semantics* are separate from Jev *hosting*. The same contract runs against TypeSafe
directly, through OpenRouter, against a self-hosted endpoint, or through your own gateway —
without changing a line of it.

## First-party providers

```csharp
services.AddTypeSafeJev(options =>
{
    options.ApiKey = configuration["TypeSafe:ApiKey"];
    options.Model = JevModel.Latest;
});

services.AddOpenRouterJev(options =>
{
    options.ApiKey = configuration["OpenRouter:ApiKey"];
    options.SiteName = "My application";
    options.ProviderOrder.Add("typesafe");
});

services.AddLocalJev(options => options.BaseAddress = new Uri("http://jev.internal:8080/"));
```

Model aliases (`JevModel.Latest`, `Fast`, `Pro`) are translated by each provider into the
identifier that host actually uses, so contracts stay portable.

## Selecting a provider

```csharp
services.AddJevClient<ITicketAI>().UseTypeSafe();
services.AddJevClient<ITicketAI>().UseOpenRouter();
services.AddJevClient<ITicketAI>().UseProvider("internal-gateway");
services.AddJevClient<ITicketAI>().UseProvider<InternalGatewayJevProvider>();
```

Precedence, highest first:

1. Programmatic per-contract configuration (`UseProvider`)
2. A method-level `Provider` on the question attribute
3. A contract-level `Provider` on `[JevClient]`
4. `JevGenOptions.DefaultProvider`
5. The first registered provider

## Writing a provider

Implement one interface. No generator changes, no new attributes, no reflection, and no change
to any contract that will run on it.

```csharp
public sealed class InternalGatewayJevProvider(HttpClient httpClient) : IJevProvider
{
    public string Name => "internal-gateway";

    public JevProviderCapabilities Capabilities =>
        JevProviderCapabilities.Noul
        | JevProviderCapabilities.Choice
        | JevProviderCapabilities.Score
        | JevProviderCapabilities.Probabilities
        | JevProviderCapabilities.MultiQuestion
        | JevProviderCapabilities.StructuredState;

    public async ValueTask<JevProviderResponse> EvaluateAsync(
        JevProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        // SerializeState prefers the metadata generated clients supply, so this stays
        // reflection-free and AOT-safe.
        var state = request.SerializeState();

        // ... translate into the gateway's protocol and map the response back ...
    }
}
```

```csharp
services.AddHttpClient<InternalGatewayJevProvider>();
services.AddJevProvider<InternalGatewayJevProvider>();
```

Declare capabilities honestly. They are what lets JevGen refuse a contract your provider cannot
serve, instead of letting it fail strangely in production.

### Named and factory registration

```csharp
services.AddJevProvider("internal", sp => sp.GetRequiredService<InternalGatewayJevProvider>());
services.AddJevProvider("internal", new MyProvider(httpClient));
services.AddJevProvider<MyProvider>("experimental");
```

Naming lets the same implementation be registered more than once with different configuration.

### Optional health

```csharp
public sealed class MyProvider : IJevProvider, IJevProviderHealth
{
    public ValueTask<JevProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        => new(JevProviderHealthResult.Healthy());
}
```

Picked up automatically by the [ASP.NET Core health check](aspnet-core.md#health-checks). Keep
it cheap: probes run constantly and an evaluation is billable.

## Capability validation

Contracts declare what they need; providers declare what they support. JevGen checks them
against each other at start-up.

```text
ITicketAI.AssessAsync requires:
- Choice
- Probabilities
- MultiQuestion

Provider "legacy-gateway" supports:
- Choice

Missing:
- Probabilities
- MultiQuestion
```

The default is to fail. **Silent degradation is never an option**: a contract returning
`ChoiceResult<T>` promises a distribution, and answering without one while pretending otherwise
is worse than not answering.

Two escape hatches, both explicit:

```csharp
// Fall back to a provider that can serve it.
services.AddJevClient<ITicketAI>().UseProvider("legacy").FallbackTo("typesafe");

// Or accept degraded semantics, deliberately, for this contract.
services.AddJevClient<ITicketAI>().AllowDegradedCapabilities();
```

## Fallback

```csharp
services.AddJevClient<ITicketAI>()
    .UseOpenRouter()
    .FallbackToTypeSafe()
    .FallbackTo<InternalGatewayJevProvider>();
```

Fallback triggers on provider unavailability, rate limiting, timeouts, unsupported capabilities
and failures classified as transient.

It never triggers on authentication failures or malformed contracts. Those fail identically
everywhere, so failing over would only spend the caller's latency budget and a second provider's
quota.

### Confidence-based fallback

```csharp
services.AddJevClient<ITicketAI>()
    .UseOpenRouter()
    .FallbackToTypeSafe()
    .FallbackWhenConfidenceBelow(0.60);
```

When every provider answers below the floor, the highest-confidence answer is returned rather
than failing — your policy decides what to do with a low-confidence result.

Provenance survives:

```csharp
result.Metadata!.Provider   // "typesafe"
result.Metadata.Model       // "jev-1"
result.Metadata.Attempts    // 2
```

## Provider-specific options

Data one host needs and the canonical contract does not model:

```csharp
services.AddJevClient<ITicketAI>()
    .ConfigureProvider("openrouter", options => options["providerOrder"] = "typesafe");
```

or by attribute:

```csharp
[JevProviderOption("openrouter", "providerOrder", "typesafe")]
public interface ITicketAI;
```

**Provider-scoped options never reach another provider.** A contract annotated for one host
stays correct on every other, which is what makes annotating it safe.

Options named `header:<name>` are sent as HTTP headers by the first-party providers.

## Chat-model providers

`JevGen.Providers.OpenAI`, `.Anthropic` and `.Gemini` run evaluation contracts on
general-purpose models through structured output.

```csharp
services.AddOpenAIEvaluation(options =>
{
    options.ApiKey = configuration["OpenAI:ApiKey"];
    options.AllowApproximateProbabilities = true;
});

services.AddJevClient<ITicketAI>().UseOpenAI();
```

These approximate Jev semantics; they do not replace them. A chat model's self-reported
distribution is not calibrated the way a purpose-built evaluation model's is, so these providers
**do not claim the `Probabilities` capability by default**. A contract that depends on
distributions fails loudly against them unless you set `AllowApproximateProbabilities`, which is
the explicit acknowledgement that approximate numbers are acceptable for your case.
