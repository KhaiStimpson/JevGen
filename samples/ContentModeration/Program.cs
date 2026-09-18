using ContentModeration;
using JevGen;
using JevGen.Testing;

[assembly: JevJsonContext(typeof(ModerationJsonContext))]

// Content moderation: where an over-confident automated decision does real harm, in both
// directions. The contract keeps the uncertainty visible so the escalation rules can use it.

var content = new Content
{
    Text = "Honestly I don't know how much longer I can keep doing this.",
    Surface = "community-forum",
    AuthorAccountAgeDays = 412,
    AuthorPriorViolations = 0,
};

var fake = JevFake.Create<IModerationAI>(runtime => runtime
    .Choice(
        "category",
        "self_harm",
        0.44,
        new Dictionary<string, double>
        {
            ["safe"] = 0.39,
            ["self_harm"] = 0.44,
            ["harassment"] = 0.05,
            ["spam"] = 0.02,
            ["other_violation"] = 0.10,
        })
    .Noul("requiresRemoval", 0.11)
    .Noul("safetyConcern", 0.58)
    .Score("reviewDifficulty", 3, 0.66));

var assessment = await fake.Client.AssessAsync(content);

Console.WriteLine("=== Moderation assessment ===");
Console.WriteLine();
Console.WriteLine($"Category:          {assessment.Category.Value} ({assessment.Category.Confidence:P0})");

foreach (var (category, probability) in assessment.Category.Probabilities.OrderByDescending(pair => pair.Value))
{
    Console.WriteLine($"  {category,-16} {probability:P0}");
}

Console.WriteLine();
Console.WriteLine($"Requires removal:  {assessment.RequiresRemoval.Probability:P0}");
Console.WriteLine($"Safety concern:    {assessment.SafetyConcern.Probability:P0}");
Console.WriteLine($"Review difficulty: {assessment.ReviewDifficulty.Value} of 4");
Console.WriteLine();

Console.WriteLine("=== What the application does with it ===");
Console.WriteLine();

// Removal and safety are separate questions with different costs, so they get different
// thresholds. Collapsing them into one "is it bad" score would lose the distinction.
if (assessment.RequiresRemoval.Value(threshold: 0.85))
{
    Console.WriteLine("Removing the content: the model is confident it breaches the policy.");
}
else
{
    Console.WriteLine("Leaving the content up: removal confidence is well below the bar.");
}

// A possible safety concern is escalated at a much lower threshold, because the cost of
// missing one is not symmetric with the cost of a false positive.
if (assessment.SafetyConcern.Value(threshold: 0.25))
{
    Console.WriteLine("Surfacing support resources and escalating to the trained response queue.");
}

if (assessment.Category.Confidence < 0.6)
{
    Console.WriteLine(
        $"Queueing for human review: the top category carries only {assessment.Category.Confidence:P0} " +
        "confidence, and the distribution is close to split.");
}
