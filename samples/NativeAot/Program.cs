using JevGen;
using JevGen.Providers.TypeSafe;
using Microsoft.Extensions.DependencyInjection;
using NativeAotSample;

[assembly: JevJsonContext(typeof(AotJsonContext))]

// A Native AOT console application.
//
//   dotnet publish -c Release -r linux-x64
//   ./bin/Release/net10.0/linux-x64/publish/JevGen.Samples.NativeAot
//
// Nothing here is special-cased for AOT. The generated client uses no reflection, question
// metadata is static data compiled into the binary, and state serialization goes through the
// context declared in Contracts.cs.

Console.WriteLine($"Reflection-based JSON: {(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault ? "enabled" : "disabled")}");
Console.WriteLine();

// Contract metadata is available without credentials, a provider or a network call.
Console.WriteLine(JevGenDebug.Describe<ITicketAI>());

var apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Set TYPESAFE_API_KEY to run a live evaluation.");
    return 0;
}

var services = new ServiceCollection();
services.AddJevGen(options => options.DefaultModel = "jev-latest");
services.AddTypeSafeJev(options => options.ApiKey = apiKey);
services.AddJevClient<ITicketAI>().UseTypeSafe();

await using var provider = services.BuildServiceProvider();

var result = await provider.GetRequiredService<ITicketAI>().RouteAsync(new Ticket
{
    Subject = "I was charged twice for my subscription",
    Body = "My card shows two identical charges on the same day.",
});

Console.WriteLine($"Department: {result.Value} ({result.Confidence:P1})");

foreach (var (department, probability) in result.Probabilities.OrderByDescending(pair => pair.Value))
{
    Console.WriteLine($"  {department,-10} {probability:P1}");
}

return 0;
