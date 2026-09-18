# Testing

## Fakes drive the real client

```csharp
var fake = JevFake.Create<ITicketAI>();
fake.Returns("route", Department.Billing, confidence: 0.94);

var result = await fake.Client.RouteAsync(ticket);

Assert.Equal(Department.Billing, result.Value);
```

`fake.Client` is the generated implementation, not a mock of the interface. Because generated
clients depend only on `IEvaluationRuntime`, substituting a scripted runtime leaves the real
request construction, option mapping and result mapping in place. Your test covers the code that
will actually run.

That distinction matters: mocking `ITicketAI` would test nothing except your own test double.

## Scripting answers

```csharp
var fake = JevFake.Create<ITicketAI>(runtime => runtime
    .Noul("urgent", 0.08)
    .Choice("department", "billing", 0.91)
    .Score("severity", 2, 0.77));
```

Or with enum members, so option identifiers never have to be restated:

```csharp
fake.Returns(
    "route",
    Department.Sales,
    confidence: 0.70,
    probabilities: new Dictionary<Department, double>
    {
        [Department.Sales] = 0.70,
        [Department.Billing] = 0.20,
        [Department.Technical] = 0.10,
    });
```

Scripting a question the contract does not declare throws immediately, naming the questions that
do exist — a renamed question fails the test rather than silently going unscripted.

## Unscripted questions

A question with no script answers with maximum uncertainty: 0.5 for a noul, a uniform
distribution for a choice, the midpoint for a score. An unconfigured question should never look
like a confident answer, because that is how a test passes for the wrong reason.

## State-dependent answers

```csharp
fake.Runtime.Answer("route", request =>
{
    var ticket = (Ticket)request.State;

    var option = ticket.Subject.Contains("refund", StringComparison.OrdinalIgnoreCase)
        ? "billing"
        : "technical";

    return new ChoiceQuestionResult("route", option, 0.99, new Dictionary<string, double> { [option] = 0.99 });
});
```

## Confidence boundaries

The most valuable thing to test is behaviour *around* a threshold:

```csharp
[Theory]
[InlineData(0.90, DecisionAction.Accept)]
[InlineData(0.89, DecisionAction.Review)]
[InlineData(0.65, DecisionAction.Review)]
[InlineData(0.64, DecisionAction.Reject)]
public async Task PolicyClassifiesOnTheBoundary(double confidence, DecisionAction expected)
{
    var fake = JevFake.Create<ITicketAI>();
    fake.Returns("decide", Department.Billing, confidence);

    var decision = await fake.Client.DecideAsync(Ticket);

    Assert.Equal(expected, decision.Action);
}
```

## Failure simulation

```csharp
fake.Runtime.TimesOut();
fake.Runtime.RateLimited(TimeSpan.FromSeconds(5));
fake.Runtime.Fails(new EvaluationProviderException("upstream down") { StatusCode = 503 });

// A slow provider, for cancellation and timeout tests.
fake.Runtime.Delay = TimeSpan.FromSeconds(10);
```

## Fixtures

Capture realistic behaviour once, replay it deterministically forever:

```json
{
  "answers": {
    "urgent":     { "probability": 0.12 },
    "department": { "choice": "technical", "probabilities": { "technical": 0.88, "billing": 0.12 } },
    "severity":   { "score": 4, "confidence": 0.81 }
  }
}
```

```csharp
var fake = JevFake.Create<ITicketAI>().LoadFixture("fixtures/ticket-routing.json");
```

### Recording

```csharp
builder.Services.AddJevGen().AddJevRecording();

// ... exercise the application against a real provider ...

var recorder = provider.GetRequiredService<RecordingEvaluationRuntime>();
recorder.Save("fixtures/ticket-routing.json");
```

Only answers are recorded. **Evaluation state is never captured**, because it routinely carries
personal or commercially sensitive data and a fixture file is exactly the kind of artefact that
ends up committed to a repository.

## Asserting on requests

```csharp
await fake.Client.RouteAsync(ticket);

var request = fake.Runtime.LastRequest!;

Assert.Equal("RouteAsync", request.MethodName);
Assert.Equal("Which department should handle this ticket?", request.Questions[0].Prompt);
Assert.Equal(3, request.Questions[0].Options.Length);
Assert.Equal(1, fake.Runtime.CallCount);
```

Useful for proving an aggregate really is one request rather than several.

## Dependency injection

One contract:

```csharp
services.AddJevFake<ITicketAI>(fake => fake.Returns("route", Department.Billing, 0.94));
```

Every contract at once:

```csharp
services.AddJevFakeRuntime(runtime => runtime.Choice("route", "billing", 0.94));
```

Both also disable start-up capability validation, since a fake setup has no provider to validate
against.

## Testing contract shape

Compile-time metadata is available without a runtime at all:

```csharp
var descriptor = JevClientRegistry.Get<ITicketAI>();

Assert.Contains(
    descriptor.Methods.Single(m => m.Name == "RouteAsync").Questions[0].Options,
    option => option.Id == "billing" && option.Criteria is { Length: > 0 });
```

A useful guard: fail the build when someone adds an enum member without criteria.
