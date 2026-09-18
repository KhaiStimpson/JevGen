using System.Collections.Immutable;
using System.Text.Json;
using JevGen;
using JevGen.AotTests;
using JevGen.Providers;
using Microsoft.Extensions.DependencyInjection;

// A Native AOT smoke test. It is published as a native binary and executed in CI, because the
// failures worth catching here — reflection over attributes, runtime generic instantiation,
// reflection-based serialization — only appear once the binary actually runs.

var failures = new List<string>();

void Check(string description, Func<bool> assertion)
{
    try
    {
        if (assertion())
        {
            Console.WriteLine($"  PASS  {description}");
        }
        else
        {
            failures.Add(description);
            Console.WriteLine($"  FAIL  {description}");
        }
    }
    catch (Exception exception)
    {
        failures.Add($"{description}: {exception.GetType().Name}: {exception.Message}");
        Console.WriteLine($"  FAIL  {description}: {exception.GetType().Name}: {exception.Message}");
    }
}

Console.WriteLine("JevGen Native AOT smoke test");
Console.WriteLine($"  Reflection-based JSON is {(JsonSerializer.IsReflectionEnabledByDefault ? "enabled" : "disabled")}");
Console.WriteLine();

// The generator's module initializer must have run without any assembly scanning.
Check("the contract registered itself at module load", () => JevClientRegistry.TryGet(typeof(ITicketAI), out _));

var descriptor = JevClientRegistry.Get<ITicketAI>();
Check("contract metadata survived trimming", () => descriptor is { Name: "ITicketAI", ContractVersion: "1" });
Check("both methods are described", () => descriptor.Methods.Length == 2);

Check(
    "enum option identifiers and criteria survived trimming",
    () => descriptor.Methods
        .Single(method => method.Name == "RouteAsync")
        .Questions[0].Options
        .Any(option => option.Id == "technical" && option.Criteria is { Length: > 0 }));

// The debug view must work without credentials, a provider or a network.
Check("the debug view renders", () => JevGenDebug.Describe<ITicketAI>().Contains("\"route\"", StringComparison.Ordinal));

// State serialization must go through the declared context, never reflection.
Check(
    "state serializes without reflection",
    () =>
    {
        var typeInfo = JevGenJson.TryGetTypeInfo(typeof(Ticket));
        return typeInfo is not null
               && JsonSerializer.Serialize(new Ticket { Subject = "Outage" }, typeInfo)
                   .Contains("Outage", StringComparison.Ordinal);
    });

// A full end-to-end evaluation through DI, the runtime and a provider.
var services = new ServiceCollection();
services.AddJevGen(options => options.ValidateOnStart = false);
services.AddSingleton<IJevProvider>(new StubProvider());
services.AddJevClient<ITicketAI>();

await using var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<ITicketAI>();

var routed = await client.RouteAsync(new Ticket { Subject = "The API is returning 500s." });

Check("dependency injection resolved a generated client", () => client is not null);
Check("the choice mapped onto its enum member", () => routed.Value == Department.Technical);
Check("confidence survived the round trip", () => Math.Abs(routed.Confidence - 0.87d) < 1e-9);
Check("the probability distribution survived", () => routed.Probabilities.Count == 3);

var assessment = await client.AssessAsync(new Ticket { Subject = "Billed twice", Body = "Please refund." });

Check("the aggregate evaluation mapped every question", () =>
    Math.Abs(assessment.Urgent.Probability - 0.2d) < 1e-9
    && assessment.Department.Value == Department.Billing
    && Math.Abs(assessment.Severity.Value - 3d) < 1e-9);

// Policies are pure computation and must work identically in a native binary.
var decision = assessment.Department.Apply(
    DecisionPolicy<Department>.AcceptAbove(0.9).ReviewBetween(0.6, 0.9));

Check("a decision policy classified the result", () => decision.Action == DecisionAction.Review);

Console.WriteLine();

if (failures.Count > 0)
{
    Console.WriteLine($"{failures.Count} check(s) failed:");

    foreach (var failure in failures)
    {
        Console.WriteLine("  - " + failure);
    }

    return 1;
}

Console.WriteLine("All Native AOT checks passed.");
return 0;

/// <summary>A provider with fixed answers, so the test needs no network or credentials.</summary>
internal sealed class StubProvider : IJevProvider
{
    public string Name => "stub";

    public JevProviderCapabilities Capabilities =>
        JevProviderCapabilities.Noul
        | JevProviderCapabilities.Choice
        | JevProviderCapabilities.Score
        | JevProviderCapabilities.Probabilities
        | JevProviderCapabilities.MultiQuestion
        | JevProviderCapabilities.StructuredState;

    public ValueTask<JevProviderResponse> EvaluateAsync(
        JevProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        // Serializing here proves the provider-side path is reflection-free too.
        _ = request.SerializeState(JevGenJson.Options);

        var results = ImmutableArray.CreateBuilder<JevQuestionResult>();

        foreach (var question in request.Questions)
        {
            results.Add(question.Kind switch
            {
                JevQuestionKind.Noul => new NoulQuestionResult(question.Id, 0.2d),
                JevQuestionKind.Score => new ScoreQuestionResult(question.Id, 3d, 0.7d),
                _ => Choice(question),
            });
        }

        return new ValueTask<JevProviderResponse>(new JevProviderResponse
        {
            Results = results.ToImmutable(),
            Metadata = new JevProviderMetadata { Provider = Name, Model = "stub-1", RequestId = "aot-1" },
        });
    }

    private static ChoiceQuestionResult Choice(JevQuestionDefinition question)
    {
        // RouteAsync asks for a technical answer; the aggregate asks for a billing one, so the
        // test can tell the two mappings apart.
        var selected = question.Id == "route" ? "technical" : "billing";
        var confidence = question.Id == "route" ? 0.87d : 0.72d;

        return new ChoiceQuestionResult(
            question.Id,
            selected,
            confidence,
            question.Options.ToDictionary(
                option => option.Id,
                option => option.Id == selected ? confidence : (1 - confidence) / 2,
                StringComparer.Ordinal));
    }
}
