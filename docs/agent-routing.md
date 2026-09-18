# Agent routing

A typed decision layer between an agent and the things it can do.

## Why not just ask the model

An agent that asks a general-purpose model "which tool should I use?" gets an answer buried in
prose, with no calibrated measure of how sure it was. You cannot write "only act when the model
is confident" against that, because there is no number to compare.

Routing through an evaluation contract makes the choice a typed value with a distribution:

```csharp
public enum AgentTool
{
    [JevOption("search", "Look something up on the public web")]
    Search,

    [JevOption("database", "Query the customer's own records and order history")]
    Database,

    [JevOption("email", "Draft or send a message on the user's behalf")]
    Email,

    [JevOption("none", "No tool is needed; answer directly")]
    None,
}

[JevClient]
public interface IAgentRouter
{
    [JevChoice("Choose the tool that best satisfies the user's request.")]
    Task<ChoiceResult<AgentTool>> SelectToolAsync(AgentContext context, CancellationToken cancellationToken = default);
}
```

```csharp
var tool = await router.SelectToolAsync(context);

if (tool.Confidence >= 0.75 && tool.Value != AgentTool.None)
{
    await InvokeAsync(tool.Value);
}
else
{
    await AskForClarificationAsync();
}
```

That threshold is the difference between an agent that asks a clarifying question and one that
confidently emails the wrong person.

## Guardrails

```csharp
[JevNoul("Should this request be blocked?")]
Task<NoulResult> ShouldBlockAsync(GuardrailContext context, CancellationToken cancellationToken = default);
```

```csharp
var verdict = await router.ShouldBlockAsync(new GuardrailContext { UserRequest = request });

if (verdict.Value(threshold: 0.5))
{
    return BlockedResponse;
}
```

Or as chat-client middleware, which decides before any tokens are produced:

```csharp
builder.Services.AddChatClient(inner => inner)
    .UseJevGuardrail(services =>
    {
        var router = services.GetRequiredService<IAgentRouter>();

        return async (messages, cancellationToken) => await router.ShouldBlockAsync(
            new GuardrailContext { UserRequest = string.Join("\n", messages.Select(m => m.Text)) },
            cancellationToken);
    },
    threshold: 0.5);
```

Because it runs before the model, nothing that should have been blocked reaches the caller
mid-stream.

## Model routing

```csharp
[JevChoice("Select the model best suited to this request.")]
Task<ChoiceResult<ModelTarget>> SelectModelAsync(AgentContext context, CancellationToken cancellationToken = default);
```

A cheap evaluation that routes most traffic to a small model, escalating only what needs a large
one. When the router itself is unsure, escalate — the cost of the larger model is lower than the
cost of a bad answer.

## Narrowing an agent's tools

```csharp
var options = new ChatOptions { Tools = allTools }
    .WithRoutedTool(tool, minimumConfidence: 0.75);
```

Restricts the tool list to the routed choice when confidence is high enough, and leaves it
untouched when it is not — narrowing on a guess would be worse than not narrowing at all.

## Dynamic evaluations

When the questions are assembled at runtime rather than declared:

```csharp
var result = await evaluationClient.EvaluateAsync(
    state,
    [
        new JevQuestionDefinition
        {
            Id = "safe",
            Kind = JevQuestionKind.Noul,
            Prompt = "Is this request safe to act on?",
        },
    ],
    cancellationToken);

var safe = result.Noul("safe");
```

Generated clients remain preferable wherever the questions are known: they are checked at
compile time and need no reflection.

## Full example

[samples/AgentToolRouting](../samples/AgentToolRouting) shows a guardrail, tool routing and
model routing together.
