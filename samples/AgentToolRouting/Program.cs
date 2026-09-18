using AgentToolRouting;
using JevGen;
using JevGen.Testing;

[assembly: JevJsonContext(typeof(AgentJsonContext))]

// Agent tool routing: a typed decision layer between an agent and the things it can do.
//
// Asking a general model to pick a tool buries the choice in prose and gives no calibrated
// measure of how sure it was. A routing contract makes the choice a typed value with a
// distribution, so the agent can require confidence before acting.

var context = new AgentContext
{
    UserRequest = "When did my last order ship, and can you chase it up?",
    RecentTurns = ["Hi, I need help with an order.", "Sure, what's the problem?"],
    HasCustomerRecord = true,
};

var fake = JevFake.Create<IAgentRouter>(runtime => runtime
    .Choice(
        "selectTool",
        "database",
        0.81,
        new Dictionary<string, double>
        {
            ["database"] = 0.81,
            ["email"] = 0.12,
            ["search"] = 0.04,
            ["calendar"] = 0.01,
            ["none"] = 0.02,
        })
    .Choice(
        "selectModel",
        "small",
        0.88,
        new Dictionary<string, double> { ["small"] = 0.88, ["large"] = 0.12 })
    .Noul("shouldBlock", 0.03));

Console.WriteLine("=== Guardrail ===");
Console.WriteLine();

var guardrail = await fake.Client.ShouldBlockAsync(
    new GuardrailContext { UserRequest = context.UserRequest, IsAuthenticated = true });

Console.WriteLine($"Block probability: {guardrail.Probability:P0}");

if (guardrail.Value(threshold: 0.5))
{
    Console.WriteLine("Blocked before reaching the model.");
    return;
}

Console.WriteLine("Allowed.");
Console.WriteLine();

Console.WriteLine("=== Tool routing ===");
Console.WriteLine();

var tool = await fake.Client.SelectToolAsync(context);

Console.WriteLine($"Tool: {tool.Value} ({tool.Confidence:P0})");

foreach (var (candidate, probability) in tool.Probabilities.OrderByDescending(pair => pair.Value))
{
    Console.WriteLine($"  {candidate,-10} {probability:P0}");
}

Console.WriteLine();

// Requiring confidence before invoking a tool is the difference between an agent that asks a
// clarifying question and one that confidently emails the wrong person.
const double ToolConfidenceFloor = 0.75;

if (tool.Confidence >= ToolConfidenceFloor && tool.Value != AgentTool.None)
{
    Console.WriteLine($"Invoking the {tool.Value} tool.");
}
else
{
    Console.WriteLine(
        $"Confidence {tool.Confidence:P0} is below the {ToolConfidenceFloor:P0} floor; " +
        "asking the user to clarify instead of guessing.");
}

Console.WriteLine();
Console.WriteLine("=== Model routing ===");
Console.WriteLine();

var model = await fake.Client.SelectModelAsync(context);

Console.WriteLine($"Model: {model.Value} ({model.Confidence:P0})");
Console.WriteLine(
    model.Value == ModelTarget.Small
        ? "Routing to the small model: cheaper and faster, and the request does not need more."
        : "Routing to the large model: the request needs multi-step reasoning.");
