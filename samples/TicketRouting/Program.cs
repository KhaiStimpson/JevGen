using JevGen;
using JevGen.Providers.TypeSafe;
using JevGen.Testing;
using Microsoft.Extensions.DependencyInjection;
using TicketRouting;

[assembly: JevJsonContext(typeof(TicketJsonContext))]

// Ticket routing: the canonical JevGen example.
//
// The contract in Contracts.cs is an ordinary C# interface. Everything below it — the request,
// the question definitions, the JSON, the result mapping — is generated at compile time.

Console.WriteLine("=== What the contract will send ===");
Console.WriteLine();
Console.WriteLine(JevGenDebug.Describe<ITicketAI>());

var ticket = new Ticket
{
    Subject = "I was charged twice for my subscription",
    Body = "My card shows two identical charges on the same day. Please refund one.",
    CustomerTier = "enterprise",
};

// Running against the real API needs only configuration; the contract does not change.
//
//   builder.Services.AddTypeSafeJev(options => options.ApiKey = configuration["TypeSafe:ApiKey"]);
//   builder.Services.AddJevClient<ITicketAI>().UseTypeSafe();
//
// This sample uses the testing package so it runs offline, with deterministic answers.
var services = new ServiceCollection();
services.AddJevGen();
services.AddJevFake<ITicketAI>(fake => fake
    .Returns(
        "route",
        Department.Billing,
        0.94,
        new Dictionary<Department, double>
        {
            [Department.Billing] = 0.94,
            [Department.Technical] = 0.04,
            [Department.Sales] = 0.02,
        })
    .ReturnsProbability("isUrgent", 0.18)
    .ReturnsProbability("urgent", 0.18)
    .Returns("department", Department.Billing, 0.94)
    .ReturnsScore("severity", 2, 0.83));

await using var provider = services.BuildServiceProvider();
var ai = provider.GetRequiredService<ITicketAI>();

Console.WriteLine("=== Routing a ticket ===");
Console.WriteLine();

var decision = await ai.RouteAsync(ticket);

Console.WriteLine($"Department: {decision.Value} ({decision.Confidence:P1} confident)");

foreach (var (department, probability) in decision.Probabilities.OrderByDescending(pair => pair.Value))
{
    Console.WriteLine($"  {department,-10} {probability:P1}");
}

Console.WriteLine();

// The confidence is not decoration. The application decides what threshold means "act".
if (decision.Confidence >= 0.90)
{
    Console.WriteLine($"Routing automatically to {decision.Value}.");
}
else
{
    Console.WriteLine("Confidence is too low to route automatically; queueing for a human.");
}

Console.WriteLine();
Console.WriteLine("=== Assessing it in one request ===");
Console.WriteLine();

// Three questions, one state, one round trip.
var assessment = await ai.AssessAsync(ticket);

Console.WriteLine($"Urgent:     {assessment.Urgent.Probability:P1} (reads as {assessment.Urgent.Value()})");
Console.WriteLine($"Department: {assessment.Department.Value}");
Console.WriteLine($"Severity:   {assessment.Severity.Value} of 5");
