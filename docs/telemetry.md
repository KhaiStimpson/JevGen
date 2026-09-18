# Telemetry

```csharp
builder.Services.AddJevGen().AddJevGenTelemetry();
```

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(JevGenTelemetry.Name))
    .WithMetrics(metrics => metrics.AddMeter(JevGenTelemetry.Name));
```

## Activities

| Name | Covers |
|---|---|
| `JevGen.Evaluate` | One end-to-end evaluation, including retries and fallbacks |
| `JevGen.ProviderCall` | One call to one provider |
| `JevGen.Policy` | One policy classification |
| `JevGen.Fallback` | A fallback to a secondary provider |

Tags include `jevgen.contract`, `jevgen.method`, `jevgen.provider`, `jevgen.model`,
`jevgen.question_count`, `jevgen.attempt`, `jevgen.contract_version` and `jevgen.request_id`.

## Metrics

| Instrument | Type | Meaning |
|---|---|---|
| `jevgen.requests` | Counter | Evaluations started |
| `jevgen.request.duration` | Histogram (s) | Evaluation duration |
| `jevgen.errors` | Counter | Failed evaluations, tagged by error type |
| `jevgen.confidence` | Histogram | Confidence of returned answers |
| `jevgen.questions` | Histogram | Questions per evaluation |
| `jevgen.retries` | Counter | Attempts beyond the first |
| `jevgen.fallbacks` | Counter | Fallbacks to a secondary provider |
| `jevgen.policy.accept` / `.review` / `.reject` | Counter | Policy classifications |

The confidence histogram is the one to watch. A model whose confidence distribution shifts
downward over a week is telling you something — about a change in your traffic, or in the
model — well before accuracy metrics would.

## What is recorded

```csharp
builder.Services.AddJevGenTelemetry(options =>
{
    options.RecordConfidence = true;      // default
    options.RecordQuestionNames = true;   // default
    options.RecordRequestIds = true;      // default
    options.RecordPrompts = false;        // default
    options.RecordAnswers = false;        // default
    options.RecordState = false;          // default
});
```

Defaults are conservative on purpose:

- **State is never recorded** unless you turn it on. It is the model's input, and routinely
  contains personal or commercially sensitive data. Turning `RecordState` on means accepting
  that it will reach your telemetry backend.
- **Prompts are not recorded** by default, because they frequently embed business rules.
- **Question identifiers are** recorded, because they describe contract shape rather than data.
- **API keys are never recorded**, under any setting. There is no option to change that.

## Logging

Structured logs are emitted through `Microsoft.Extensions.Logging`:

| Level | Event |
|---|---|
| Debug | A completed evaluation with its duration |
| Information | A confidence-triggered fallback |
| Warning | A provider failure, a capability mismatch under warn mode, a retry |

## Auditing

For a durable record of decisions, rather than sampled telemetry:

```csharp
public sealed record DecisionAudit
{
    public required string Contract { get; init; }
    public required string Method { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required double Confidence { get; init; }
    public string? ContractVersion { get; init; }
    public string? RequestId { get; init; }
    public DecisionAction? Action { get; init; }
}
```

Implement `IDecisionAuditSink` and write an `IEvaluationFilter` that records to it; see
[dependency-injection.md](dependency-injection.md#custom-filters). `ContractVersion` is what
makes it possible to compare decisions across contract revisions.

State recording stays opt-in here for the same reasons.
