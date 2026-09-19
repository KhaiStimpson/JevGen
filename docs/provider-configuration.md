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

## Endpoints and models

Jev is a **decisions model**, not a chat model. It is served on its own endpoint, speaking the
System One schema — one state, many named questions, typed answers with probabilities — and the
chat endpoints reject it outright:

```text
typesafe/jev-1.13 is a decisions model and cannot be used with the chat/completions
endpoint. Use the /api/alpha/decisions endpoint instead.
```

| | Endpoint | `JevModel.Latest` resolves to |
|---|---|---|
| **OpenRouter** | `POST https://openrouter.ai/api/alpha/decisions` | `~typesafe/jev-latest` |
| **TypeSafe** | `POST https://api.typesafe.ai/v1/systemone` | `jev-latest` |

An identifier that is not a `JevModel` alias is sent unchanged, so a build can be pinned:

```csharp
services.AddOpenRouterJev(options =>
{
    options.ApiKey = configuration["OpenRouter:ApiKey"];
    options.Model = "typesafe/jev-1.13-20260917";   // a pinned build
});
```

On OpenRouter, `~typesafe/jev-latest` is the floating alias — the leading tilde is OpenRouter's
marker for an alias rather than a version — `typesafe/jev-1.13` is a version, and
`typesafe/jev-1.13-20260917` a pinned build. Jev does not appear in the default
`GET /api/v1/models` listing; its modality is `text->decisions`. On TypeSafe, `GET /v1/models`
lists what that host accepts.

> **`JevModel.Fast` and `JevModel.Pro` are not currently served by any host.** Neither TypeSafe
> nor OpenRouter publishes a latency or quality tier. Asking for one fails with a message saying
> so, before a request is sent — a provider that quietly sent an invented identifier instead
> would return an opaque 404. The constants remain so a contract that names one keeps compiling
> if the tiers appear.

> **The TypeSafe endpoint is unverified against the live service.** Its path and model naming are
> read from the source of the official `typesafe_sdk` 0.7.0 Python package, whose
> `prepare_system_one` posts `{state, model, questions}` to `/v1/systemone`. Confirming it needs
> an early-access key. The OpenRouter provider is verified against the live decisions endpoint.

## Evaluation metadata

Every System One host reports what the evaluation consumed, and the gateways report who served
it and what it cost:

```csharp
var properties = result.Metadata!.Properties;

properties["usage.inputTokens"]    // 430
properties["usage.outputTokens"]   // 79
properties["usage.cost"]           // 0.00001806, where the host prices the call
properties["provider"]             // "TypeSafe" — the upstream host that answered
```

OpenRouter mirrors the last two under `openrouter.cost` and `openrouter.provider`, alongside
`openrouter.model`.

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
result.Metadata.Model       // "typesafe/jev-1.13-20260917" — the build that answered
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
