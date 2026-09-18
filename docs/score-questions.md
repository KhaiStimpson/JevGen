# Score questions

A *score* asks for a numeric rating on a declared scale.

```csharp
[JevScore("Rate the severity of this incident.", Min = 1, Max = 5)]
Task<ScoreResult> SeverityAsync(Incident incident, CancellationToken cancellationToken = default);
```

## Declaring the scale

Every score question needs a scale. A bare "rate this" leaves the model to invent one, and
[JEV006](diagnostics.md#jev006) rejects it at compile time.

**Bounds:**

```csharp
[JevScore("Rate the severity.", Min = 1, Max = 5)]
```

**Rubric labels**, ordered from lowest to highest:

```csharp
[JevScore("Rate severity.", "Low", "Medium", "High", "Critical")]
```

With labels and no explicit bounds, the scale runs from 1 to the number of labels. Labels
usually produce more consistent scoring than bare numbers, because "3 out of 5" means whatever
the model decides it means, whereas "High" is anchored.

## Reading the result

```csharp
var severity = await ai.SeverityAsync(incident);

severity.Value             // 4
severity.ConfidenceOrNull  // 0.79, or null if the provider did not report one
severity.Confidence        // 0.79, or 1 when none was reported
severity.Normalize(1, 5)   // 0.75
```

`ConfidenceOrNull` and `Confidence` are separate deliberately: not every provider reports
confidence on a score, and a missing value is different from a low one.

`Normalize` rescales onto `[0, 1]` for comparing scores from different scales. It is not a
probability.

## On properties

```csharp
public sealed record TicketAssessment
{
    [JevScore("Rate the severity.", Min = 1, Max = 5)]
    public required ScoreResult Severity { get; init; }
}
```

## Primitive opt-in

```csharp
[JevScore("Rate the severity.", Min = 1, Max = 5, AllowPrimitiveResult = true)]
Task<double> SeverityAsync(Incident incident, CancellationToken cancellationToken = default);
```

## Wire shape

```json
{
  "type": "score",
  "question": "Rate severity.",
  "min": 1,
  "max": 4,
  "criteria": ["Low", "Medium", "High", "Critical"]
}
```
