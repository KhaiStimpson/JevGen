# Noul questions

A *noul* asks whether a proposition holds, and answers with a probability.

```csharp
[JevNoul("Does this ticket require urgent attention?")]
Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

## Reading the result

`NoulResult` does not implicitly convert to `bool`, and that is on purpose. A model that says
0.51 and one that says 0.99 are not saying the same thing, and the threshold at which you should
act depends on what acting costs.

```csharp
var urgent = await ai.IsUrgentAsync(ticket);

urgent.Probability      // 0.83
urgent.Value()          // true, at the default 0.5 threshold
urgent.Value(0.95)      // false, at a stricter one
urgent.Confidence       // 0.83: distance from maximum uncertainty
```

`Confidence` is `max(p, 1 - p)`. A probability of 0.02 is a *confident* "no", not an unconfident
answer, and treating it as low confidence would be wrong.

## Choosing thresholds

Thresholds encode what a mistake costs, and the two directions are rarely symmetric.

```csharp
var assessment = await moderationAI.AssessAsync(content);

// Removing content wrongly is a visible, damaging error, so require near-certainty.
if (assessment.RequiresRemoval.Value(threshold: 0.85))
{
    Remove(content);
}

// Missing a genuine safety concern is far worse than an unnecessary check, so escalate early.
if (assessment.SafetyConcern.Value(threshold: 0.25))
{
    Escalate(content);
}
```

Keeping the probability rather than a boolean is what makes this possible at all.

## Wire shape

```json
{ "type": "noul", "instructions": "Does this ticket require urgent attention?" }
```

```json
{ "type": "noul", "noul": 0.14 }
```

The provider answers with a probability. A response without one is rejected rather than
defaulted ([troubleshooting](troubleshooting.md)).

## On properties

Inside an aggregate evaluation:

```csharp
public sealed record TicketAssessment
{
    [JevNoul("Does this require urgent attention?")]
    public required NoulResult Urgent { get; init; }
}
```

## Primitive opt-in

```csharp
[JevNoul("Is it urgent?", AllowPrimitiveResult = true)]
Task<bool> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
```

Read at a fixed 0.5 threshold, with the probability discarded. Reach for it only when you are
sure nothing downstream will ever want to know how close the call was.
