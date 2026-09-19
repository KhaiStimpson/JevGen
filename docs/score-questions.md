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

Jev takes no bounds. It takes an ordered list of level descriptions, and answers with a
**zero-based level** over them:

```json
{
  "type": "score",
  "instructions": "Rate severity.",
  "criteria": ["Low", "Medium", "High", "Critical"]
}
```

```json
{
  "type": "score",
  "score": 2.4,
  "legend": { "0": "Low", "1": "Medium", "2": "High", "3": "Critical" },
  "probabilities": { "0": 0.02, "1": 0.12, "2": 0.30, "3": 0.56 },
  "confidence": 0.81
}
```

The score is the probability-weighted average of the levels, so it falls between them.

### How the scale is preserved

`ScoreResult.Value` is on the scale your contract declared, not on Jev's levels. JevGen maps
between them:

```text
value = Min + level / (levels - 1) × (Max - Min)
```

The legend the host returns decides `levels`, since it reports what the answer was actually
measured over. Above, level 2.4 of four levels is 0.8 of the way up, which on the 1..4 scale the
four labels imply is **3.4**.

When a question declares bounds but no rubric, Jev still requires criteria, so JevGen generates
them — one label per level, each naming the scale value it stands for:

```csharp
[JevScore("Rate the severity.", Min = 1, Max = 5)]
```

```json
{ "type": "score", "instructions": "Rate the severity.", "criteria": ["1", "2", "3", "4", "5"] }
```

That adds no meaning the contract did not declare, and it round-trips exactly. A wide range is
sampled across at most 21 levels rather than given one label per unit — `Min = 0, Max = 100`
becomes `["0", "5", "10", … "100"]` — which costs nothing, because the answer is a weighted
average and still lands between them.

Naming the levels yourself gives the model something to judge against, so prefer criteria to
bare bounds where the scale means something.
