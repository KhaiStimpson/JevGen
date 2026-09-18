# Choice questions

A *choice* selects one option from a fixed set, and reports a probability for every candidate.

```csharp
[JevChoice("Which department should handle this ticket?")]
Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

## Describing the options

```csharp
public enum Department
{
    [JevOption("billing", "Invoices, payments, subscriptions and refunds")]
    Billing,

    [JevOption("technical", "Software defects, outages and technical support")]
    Technical,

    [JevOption("sales", "Pricing questions, upgrades and new business")]
    Sales,
}
```

`[JevOption]` takes a wire identifier and criteria. Both matter:

- The **identifier** is what the provider sees and returns. Setting it explicitly means renaming
  a C# member does not silently change the wire format.
- The **criteria** are what the model is told about when to pick the option. This is the single
  highest-leverage thing in a routing contract, and leaving it out is why
  [JEV007](diagnostics.md#jev007) is a warning rather than a suggestion.

`[JevOption]` is optional. Without it the member name is camel-cased and no criteria are sent.

Exclude sentinel members a model should never select:

```csharp
[JevOption("unknown", Exclude = true)]
Unknown,
```

## Reading the result

```csharp
var result = await ai.RouteAsync(ticket);

result.Value                                // Department.Billing
result.Confidence                           // 0.94
result.Probabilities                        // every option
result.ProbabilityOf(Department.Technical)  // 0.04
```

The distribution is often more informative than the top answer:

```csharp
// Elevated at 61% looks reassuring until you notice 31% on High.
var high = assessment.Risk.ProbabilityOf(RiskLevel.High);

if (high > 0.25)
{
    EscalateForReview();
}
```

## Probability requirements

Because `ChoiceResult<T>` exposes a distribution, JevGen requires the provider to supply one by
default. A provider that returns only a selected option is rejected, rather than quietly
answering with an empty distribution.

Relax it where a bare selection really is enough:

```csharp
[JevChoice("Which department should handle this?", RequireProbabilities = false)]
Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

This also affects which providers can serve the contract; see
[provider-configuration.md](provider-configuration.md#capability-validation).

## Decisions

Return `Decision<T>` to have a policy classify the result:

```csharp
[DecisionPolicy(AcceptAbove = 0.90, ReviewAbove = 0.65)]
[JevChoice("Which department should handle this ticket?")]
Task<Decision<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

```csharp
var decision = await ai.RouteAsync(ticket);

decision.Value       // Department.Billing
decision.Confidence  // 0.72
decision.Action      // DecisionAction.Review
```

The policy classifies. It never acts; see [policies.md](policies.md).

## Option mapping

Generated mapping matches the declared identifier first, then falls back to a
case-insensitive comparison against both the identifier and the member name, because providers
do not always echo casing exactly. An answer matching nothing raises
`EvaluationResponseException` rather than silently defaulting to the first member.

## Wire shape

```json
{
  "type": "choice",
  "question": "Which department should handle this ticket?",
  "options": {
    "billing": "Invoices, payments, subscriptions and refunds",
    "technical": "Software defects, outages and technical support",
    "sales": "Pricing questions, upgrades and new business"
  },
  "probabilities": true
}
```
