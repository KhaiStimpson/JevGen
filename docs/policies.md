# Decision policies

A policy turns a confidence level into a classification: accept, review or reject.

```csharp
var result = await ai.RouteAsync(ticket);

var decision = result.Apply(
    DecisionPolicy<Department>
        .AcceptAbove(0.90)
        .ReviewBetween(0.65, 0.90));

decision.Action      // DecisionAction.Review
decision.Value       // Department.Billing
decision.Confidence  // 0.72
```

## Policies classify; they do not act

This is the rule the design is built around. `Accept` does not settle a payment, and `Reject`
does not delete anything. The application decides what each classification means:

```csharp
switch (decision.Action)
{
    case DecisionAction.Accept:
        await SettleAsync(payment);
        break;

    case DecisionAction.Review:
        await OpenReviewCaseAsync(payment, decision);
        break;

    case DecisionAction.Reject:
        await DeclineAsync(payment);
        break;
}
```

Keeping evaluation separate from control flow is what makes the boundary auditable. A reader can
see exactly where a model's opinion becomes an action, and there is no configuration change that
can quietly cause a side effect.

## Declaring a policy

**By attribute**, when thresholds are genuinely part of the contract:

```csharp
[DecisionPolicy(AcceptAbove = 0.90, ReviewAbove = 0.65)]
[JevChoice("Which department should handle this ticket?")]
Task<Decision<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

Invalid thresholds are a compile error ([JEV016](diagnostics.md#jev016)).

**Programmatically**, which is usually better. Thresholds are business rules; they change on a
different schedule from the code that generates the client, and they often differ by tenant,
region or risk appetite:

```csharp
public sealed class RoutingPolicy(IOptionsMonitor<RoutingOptions> options)
{
    public Decision<Department> Classify(ChoiceResult<Department> result)
        => result.Apply(
            DecisionPolicy<Department>
                .AcceptAbove(options.CurrentValue.AcceptThreshold)
                .ReviewBetween(options.CurrentValue.ReviewThreshold, options.CurrentValue.AcceptThreshold));
}
```

## Applying to any result

```csharp
choiceResult.Apply(DecisionPolicy<Department>.Default);
noulResult.Apply(DecisionPolicy<bool>.Default);           // reads the proposition at 0.5
scoreResult.Apply(DecisionPolicy<double>.Default);
decision.Apply(strictPolicy);                             // reclassify under a different policy
```

For a noul, the confidence is the distance from maximum uncertainty, so a 0.02 probability is
treated as a confident "no" rather than an unconfident answer.

## Several policies over one evaluation

Because a policy is applied to a result rather than baked into it, the same evaluation can be
read under different appetites for risk:

```csharp
var standard = assessment.Risk.Apply(DecisionPolicy<RiskLevel>.AcceptAbove(0.90).RejectBelow(0.65));
var cautious = assessment.Risk.Apply(DecisionPolicy<RiskLevel>.AcceptAbove(0.98).RejectBelow(0.85));
```

## Consensus

When several providers evaluate the same state:

```csharp
var consensus = ConsensusAggregator.Aggregate(
    [
        new ProviderDecision<Department> { Provider = "typesafe",   Value = Department.Billing,   Confidence = 0.91 },
        new ProviderDecision<Department> { Provider = "openrouter", Value = Department.Billing,   Confidence = 0.88 },
        new ProviderDecision<Department> { Provider = "gateway",    Value = Department.Technical, Confidence = 0.62 },
    ],
    minimumAgreement: 0.66,
    strategy: ConsensusStrategy.Majority);

consensus.Value         // Department.Billing
consensus.Agreement     // 0.667
consensus.Confidence    // 0.895, averaged across the agreeing providers
consensus.HasConsensus  // true
```

Agreement and confidence are reported separately on purpose. Three providers that all answer
with low confidence agree completely and are still not trustworthy; collapsing the two signals
into one number would hide exactly that case.

Strategies:

| Strategy | Picks |
|---|---|
| `Majority` | The value the most providers chose |
| `ConfidenceWeighted` | The value with the highest total confidence |
| `PrimaryWithVerification` | The first provider's value unless the verifiers fail to reach the bar |
