# Defining clients

## The interface

```csharp
[JevClient]
public interface ITicketAI
{
    [JevChoice("Which department should handle this ticket?")]
    Task<ChoiceResult<Department>> RouteAsync(
        Ticket ticket,
        CancellationToken cancellationToken = default);
}
```

Rules:

1. The interface must be public or internal.
2. It must not be generic ([JEV013](diagnostics.md#jev013)).
3. Every method returns `Task<T>` or `ValueTask<T>` ([JEV002](diagnostics.md#jev002)).
4. Exactly one parameter is the state ([JEV003](diagnostics.md#jev003), [JEV004](diagnostics.md#jev004)).
5. At most one `CancellationToken` ([JEV010](diagnostics.md#jev010)), conventionally last ([JEV011](diagnostics.md#jev011)).
6. The state must be serializable ([JEV012](diagnostics.md#jev012)).
7. The return type must map onto a supported result shape.

## `[JevClient]` options

```csharp
[JevClient(
    Name = "TicketRouter",                  // the name used in telemetry and audit records
    Version = "2",                          // a contract version, for auditing and rollout tracking
    Model = "typesafe/jev-1.13-20260917",   // a model override for every method
    Provider = "openrouter")]               // a provider override for every method
public interface ITicketAI;
```

Programmatic configuration takes precedence over these attributes where it is set explicitly, so
operational decisions can change without touching contract code:

```csharp
services.AddJevClient<ITicketAI>().UseProvider("internal-gateway");
```

## State and context

With one parameter, it is the state:

```csharp
Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

Once there is more than one, say which is which:

```csharp
Task<ChoiceResult<Department>> RouteAsync(
    [State] Ticket ticket,
    [Context("region")] string region,
    [Context("customerTier")] CustomerTier tier,
    CancellationToken cancellationToken = default);
```

produces:

```json
{
  "ticket": { "subject": "...", "body": "..." },
  "region": "AU",
  "customerTier": "enterprise"
}
```

Each part is serialized with its own metadata, so composed state stays reflection-free and
AOT-safe.

Use `[State(Name = "ticket")]` to nest a single state parameter under a name rather than
spreading its properties at the root.

## Question types

| Attribute | Returns | Documented in |
|---|---|---|
| `[JevNoul]` | `NoulResult` | [noul-questions.md](noul-questions.md) |
| `[JevChoice]` | `ChoiceResult<TEnum>` or `Decision<TEnum>` | [choice-questions.md](choice-questions.md) |
| `[JevScore]` | `ScoreResult` | [score-questions.md](score-questions.md) |
| `[JevEvaluate]` | an aggregate result type | [aggregate-evaluations.md](aggregate-evaluations.md) |

## Question identifiers

Identifiers default to the camel-cased member name with any `Async` suffix removed, so
`RouteAsync` becomes `route`. Set them explicitly to keep the wire format stable across
refactors:

```csharp
[JevChoice("Which department should handle this?", Id = "department")]
Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

Identifiers must be unique within one evaluation ([JEV008](diagnostics.md#jev008)). Different
methods may reuse one, because each builds its own request.

## Prompts must be compile-time constants

Attributes take constants, so share prompts through a constants class:

```csharp
public static class TicketQuestions
{
    public const string Route = "Which department should handle this ticket?";
}

[JevChoice(TicketQuestions.Route)]
Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

## Sensitive state

```csharp
public sealed record Transaction
{
    [JevSensitive]
    public required string CardholderEmail { get; init; }
}
```

The property is still sent — it is part of what the model reasons about — but it is replaced
with `[redacted]` in anything written to logs, traces or debug output. The names are collected at
compile time, so redaction needs no reflection and works under Native AOT. Telemetry does not
record state at all unless you explicitly turn it on; see [telemetry.md](telemetry.md).

## Primitive returns

Off by default, because collapsing a probabilistic answer into a primitive throws away the
confidence and distribution your application needs to decide how much to trust it. Where that
information genuinely has no use:

```csharp
[JevNoul("Is it urgent?", AllowPrimitiveResult = true)]
Task<bool> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

`bool` is read at a 0.5 threshold, an enum takes the selected option, and a numeric type takes
the score.

## What gets generated

For `ITicketAI` in namespace `Support`, JevGen emits into `JevGen.Generated.Support`:

| Type | Role |
|---|---|
| `ITicketAI_JevGenClient` | The implementation, depending only on `IEvaluationRuntime` |
| `ITicketAI_JevGenSchema` | Cached question metadata, request construction and result mapping |
| `ITicketAI_JevGenRegistration` | A module initializer that registers the contract |

Generated types are internal and namespaced by the contract's own namespace, so two contracts
with the same name in different namespaces never collide. Generated type names are not part of
the public API and may change.

To read the generated code:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
```
