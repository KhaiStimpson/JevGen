using System.Text.Json.Serialization;
using JevGen;

namespace FraudDetection;

public enum RiskLevel
{
    [JevOption("low", "Consistent with the customer's established behaviour")]
    Low,

    [JevOption("elevated", "Unusual, but explainable by travel, a new device or a large purchase")]
    Elevated,

    [JevOption("high", "Strong indicators of account takeover or stolen card use")]
    High,
}

public sealed record Transaction
{
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public required string MerchantCategory { get; init; }

    public required string Country { get; init; }

    public required bool NewDevice { get; init; }

    public required int MinutesSinceLastTransaction { get; init; }

    /// <summary>
    /// The model reasons about the cardholder, but this must never reach logs or telemetry.
    /// </summary>
    [JevSensitive]
    public required string CardholderEmail { get; init; }
}

public sealed record FraudAssessment
{
    [JevChoice("How risky is this transaction?")]
    public required ChoiceResult<RiskLevel> Risk { get; init; }

    [JevNoul("Does this transaction show signs of account takeover?")]
    public required NoulResult AccountTakeover { get; init; }

    [JevScore(
        "Rate how far this transaction departs from the customer's usual behaviour.",
        Min = 0,
        Max = 10)]
    public required ScoreResult Anomaly { get; init; }
}

[JevClient(Version = "2")]
public interface IFraudAI
{
    /// <summary>Assesses a transaction across three dimensions in a single evaluation.</summary>
    [JevEvaluate]
    Task<FraudAssessment> AssessAsync(Transaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies risk directly into an action, using thresholds tuned for this business.
    /// </summary>
    /// <remarks>
    /// The policy classifies only. Blocking a payment stays an explicit decision in
    /// application code.
    /// </remarks>
    [DecisionPolicy(AcceptAbove = 0.95, ReviewAbove = 0.70)]
    [JevChoice("How risky is this transaction?")]
    Task<Decision<RiskLevel>> ClassifyAsync(Transaction transaction, CancellationToken cancellationToken = default);
}

[JsonSerializable(typeof(Transaction))]
internal sealed partial class FraudJsonContext : JsonSerializerContext;
