# Aggregate evaluations

A major capability of Jev is answering several questions about the *same* state in one request.
`[JevEvaluate]` makes that the natural thing to write.

```csharp
public sealed record TicketAssessment
{
    [JevNoul("Does this require urgent attention?")]
    public required NoulResult Urgent { get; init; }

    [JevChoice("Which department should handle it?")]
    public required ChoiceResult<Department> Department { get; init; }

    [JevScore("Rate the severity.", Min = 1, Max = 5)]
    public required ScoreResult Severity { get; init; }
}

[JevClient]
public interface ITicketAI
{
    [JevEvaluate]
    Task<TicketAssessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
}
```

```csharp
var assessment = await ai.AssessAsync(ticket);

assessment.Urgent.Probability     // 0.14
assessment.Department.Value       // Department.Technical
assessment.Severity.Value         // 4
```

## One request, not three

All three questions go in a single evaluation:

```json
{
  "model": "~typesafe/jev-latest",
  "state": { "subject": "...", "body": "..." },
  "questions": {
    "urgent":     { "type": "noul",   "instructions": "Does this require urgent attention?" },
    "department": { "type": "choice", "instructions": "Which department should handle it?",
                    "criteria": { "billing": "...", "technical": "...", "sales": "..." } },
    "severity":   { "type": "score",  "instructions": "Rate the severity.",
                    "criteria": ["1", "2", "3", "4", "5"] }
  }
}
```

Three separate methods would mean three round trips, three times the cost, and — because the
model sees the state three times independently — answers that can contradict one another.

## Result types

The result must be a class or record with:

- an accessible parameterless constructor (records and `required init` properties are fine), and
- properties carrying question attributes, or nested result types.

Property-based declaration is used rather than constructor parameters because it keeps the
question next to the thing it fills, and because positional records have no parameterless
constructor for the generated mapper to use.

A `required` property that carries no question and is not a nested result type is an error
([JEV009](diagnostics.md#jev009)) — it could never be populated.

## Nested results

```csharp
public sealed record RiskBreakdown
{
    [JevNoul("Is the customer likely to churn?")]
    public required NoulResult Churn { get; init; }

    [JevScore("Rate the financial exposure.", Min = 0, Max = 10)]
    public required ScoreResult Exposure { get; init; }
}

public sealed record TicketAssessment
{
    [JevChoice("Which department should handle it?")]
    public required ChoiceResult<Department> Department { get; init; }

    public required RiskBreakdown Risk { get; init; }
}
```

Nested questions get dotted identifiers — `risk.churn`, `risk.exposure` — so they stay unique
and readable on the wire. Nesting can go as deep as you like; a cycle is caught at compile time.

## Identifiers

Defaults come from the property name, camel-cased. Override them to keep the wire format stable:

```csharp
[JevNoul("Does this require urgent attention?", Id = "urgency")]
public required NoulResult Urgent { get; init; }
```

They must be unique within the evaluation ([JEV008](diagnostics.md#jev008)).

## Capability requirements

An aggregate requires `MultiQuestion`, plus whatever its individual questions need. A provider
that cannot evaluate several questions at once is refused rather than being called three times
behind your back — that would change the cost and the semantics without saying so. See
[provider-configuration.md](provider-configuration.md#capability-validation).
