# Architecture

## The shape of it

```text
Application
    |
    v
Annotated C# contract
    |
    v
JevGen incremental generator          (compile time)
    |
    +--> generated client
    +--> cached question metadata
    +--> result mapping
    +--> registration
    +--> diagnostics
    |
    v
IEvaluationRuntime                    (run time)
    |
    +--> filter pipeline: telemetry, resilience, audit
    +--> provider selection and capability validation
    +--> fallback orchestration
    |
    v
IJevProvider
    |
    +--> TypeSafe   +--> OpenRouter   +--> self-hosted   +--> your own
```

## Packages

| Package | Contains |
|---|---|
| `JevGen.Abstractions` | Result types, question model, runtime contracts, error model. No provider dependency. |
| `JevGen` | Attributes, the client registry, the runtime, JSON plumbing, the debug view. What consumers install. |
| `JevGen.Generator` | The incremental generator. Ships as an analyzer asset. |
| `JevGen.Analyzers` / `.CodeFixes` | Whole-compilation rules and code fixes. |
| `JevGen.DependencyInjection` | Registration, builders, resilience. |
| `JevGen.Providers.Abstractions` | The provider SPI. |
| `JevGen.Jev` | The Jev wire protocol, shared by the first-party providers. |
| `JevGen.Providers.*` | TypeSafe, OpenRouter, Local, and the chat-model providers. |
| `JevGen.Policies` | Decision policies and consensus. |
| `JevGen.Testing` | Scripted runtimes, fixtures, recording. |
| `JevGen.OpenTelemetry` | Activities, metrics, logging. |
| `JevGen.AspNetCore` | Health checks, problem details, endpoint filters. |
| `JevGen.Extensions.AI` | Guardrails, tool routing, dynamic evaluation. |
| `JevGen.Cli` | `jevgen inspect`, `validate`, `doctor`. |

## Key decisions

### Source generation, not runtime proxies

Compile-time validation, Native AOT and trimming support, better IDE diagnostics, predictable
performance, and generated code you can read. A dynamic proxy would trade all of that for
slightly less machinery.

### Preserve uncertainty

Core results are `ChoiceResult<T>`, `NoulResult`, `ScoreResult` — never a bare primitive unless
you ask. Confidence and probability distributions are the core of an AI decision system, and an
API that discards them leaves callers unable to tell a confident answer from a coin flip.

Confidence is exposed as `double` for API simplicity. A validated `Probability` value type
exists for applications that want one on their own boundaries.

### Generated clients target one abstraction

Generated code depends only on `IEvaluationRuntime`. That is what lets testing, resilience,
telemetry, provider routing and fallback all change without regenerating or altering a single
application-facing API.

### Jev semantics are separate from Jev hosting

A contract does not know whether it runs against TypeSafe, OpenRouter, a company gateway or a
self-hosted endpoint. Only registration changes.

### Providers are a public extension point

`IJevProvider` and the canonical request and response models are public, documented and
versioned. A third party ships a provider as an independent package without forking JevGen,
modifying the generator, or waiting for first-party support.

### Policies classify; they never act

A policy turns confidence into `Accept`, `Review` or `Reject`. Executing the consequence stays
in application code, which keeps the boundary between a model's opinion and a business action
visible and auditable.

### No silent degradation

A contract that requires semantics its provider lacks fails at start-up. The only alternatives
are an explicit fallback or an explicit opt-in to degraded behaviour. Quietly answering with
less than the contract promises is never one of the options.

### No runtime assembly scanning

Registration comes from generated module initializers and a static registry. There is no
`AddAllJevClients(Assembly)` overload, because scanning is exactly what breaks trimming and AOT.

## Incremental generation

The pipeline is: a syntax predicate narrows to attributed interfaces, a semantic transform
produces an immutable, value-equal contract model, and emitters turn that model into source.
Nothing downstream of the model touches a Roslyn symbol.

Diagnostics travel *with* the model rather than being reported from the transform, because
transform results are cached and diagnostics reported during a cached step would be dropped.
They are captured as value-equal snapshots holding a file path and span, so a `Location` never
roots a whole compilation in the generator's cache.

A test asserts that editing an unrelated file leaves JevGen's own pipeline steps fully cached.

## Serialization

Generated clients supply `JsonTypeInfo` for their state, so providers serialize without
reflection.

Because source generators cannot read each other's output, JevGen cannot emit
`[JsonSerializable]` and have `System.Text.Json` process it. Applications declare a context and
name it with `[assembly: JevJsonContext]`. Without one, JevGen falls back to reflection where
the runtime still allows it, and says so through [JEV020](diagnostics.md#jev020).

Composed state — a state parameter plus `[Context]` values — is serialized part by part into a
`JsonElement` map described by JevGen's own generated context, so composition stays AOT-safe.

## Generated names

`Support.ITicketAI` generates into `JevGen.Generated.Support`:

```text
ITicketAI_JevGenClient
ITicketAI_JevGenSchema
ITicketAI_JevGenRegistration
```

Namespacing by the contract's own namespace is what prevents collisions between same-named
contracts, without resorting to identity hashes.

Generated types are internal, and their names are not part of the public API.

## Breaking-change strategy

**Before 1.0:** the public API may iterate, with migration notes. The generated source format is
internal.

**From 1.0:** semantic versioning. Attributes and runtime interfaces are stable. Generated
implementation types remain non-public, and generated source names carry no compatibility
guarantee.

## Performance

- No reflection during request execution
- No dynamic code generation
- Question metadata built once into static readonly fields, never per call
- Attributes parsed at compile time, never at run time
- Generated serializer metadata

Generated-client overhead relative to calling a provider directly is a record allocation and a
dictionary lookup per question.
