using FraudDetection;
using JevGen;
using JevGen.Testing;

[assembly: JevJsonContext(typeof(FraudJsonContext))]

// Fraud detection: the case where preserving uncertainty matters most.
//
// A fraud model that answers "fraud: true" is unusable. What a payments system needs is the
// probability, the distribution across risk levels, and a policy that says what each band means.

var transaction = new Transaction
{
    Amount = 2_480.00m,
    Currency = "USD",
    MerchantCategory = "electronics",
    Country = "RO",
    NewDevice = true,
    MinutesSinceLastTransaction = 3,
    CardholderEmail = "customer@example.com",
};

var fake = JevFake.Create<IFraudAI>(runtime => runtime
    .Choice(
        "risk",
        "elevated",
        0.61,
        new Dictionary<string, double> { ["low"] = 0.08, ["elevated"] = 0.61, ["high"] = 0.31 })
    .Noul("accountTakeover", 0.42)
    .Score("anomaly", 7.5, 0.77)
    .Choice(
        "classify",
        "elevated",
        0.61,
        new Dictionary<string, double> { ["low"] = 0.08, ["elevated"] = 0.61, ["high"] = 0.31 }));

var assessment = await fake.Client.AssessAsync(transaction);

Console.WriteLine("=== Transaction assessment ===");
Console.WriteLine();
Console.WriteLine($"Risk:             {assessment.Risk.Value} ({assessment.Risk.Confidence:P0})");

foreach (var (level, probability) in assessment.Risk.Probabilities.OrderByDescending(pair => pair.Value))
{
    Console.WriteLine($"  {level,-10} {probability:P0}");
}

Console.WriteLine($"Account takeover: {assessment.AccountTakeover.Probability:P0}");
Console.WriteLine($"Anomaly:          {assessment.Anomaly.Value} of 10");
Console.WriteLine();

// The single most important line in this sample. The model said "elevated" with 61% confidence,
// but 31% of the probability mass sits on "high". A system that collapsed this to the top answer
// would discard exactly the signal that should trigger review.
var highRisk = assessment.Risk.ProbabilityOf(RiskLevel.High);

Console.WriteLine($"Probability the transaction is actually high risk: {highRisk:P0}");
Console.WriteLine();

Console.WriteLine("=== Applying a policy ===");
Console.WriteLine();

var classified = await fake.Client.ClassifyAsync(transaction);

Console.WriteLine($"Policy action: {classified.Action}");
Console.WriteLine();

// JevGen classifies. Acting is the application's job, and stays visible here in the code that
// owns the consequence.
switch (classified.Action)
{
    case DecisionAction.Accept:
        Console.WriteLine("Settling the payment.");
        break;

    case DecisionAction.Review:
        Console.WriteLine("Holding the payment and opening a review case.");
        break;

    case DecisionAction.Reject:
        Console.WriteLine("Declining the payment and notifying the cardholder.");
        break;
}

// A second policy, tuned to a different appetite for risk, over the same evaluation.
var conservative = assessment.Risk.Apply(
    DecisionPolicy<RiskLevel>.AcceptAbove(0.85).ReviewBetween(0.40, 0.85));

Console.WriteLine();
Console.WriteLine($"Under a more conservative policy the same result is: {conservative.Action}");
