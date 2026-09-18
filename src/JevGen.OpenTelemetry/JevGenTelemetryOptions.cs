namespace JevGen.Telemetry;

/// <summary>
/// What JevGen's instrumentation is allowed to record.
/// </summary>
/// <remarks>
/// Defaults are deliberately conservative. Evaluation state is the model's input and routinely
/// carries personal or commercially sensitive data, so it is never recorded unless an
/// application explicitly turns it on. API keys are never recorded at all, under any setting.
/// </remarks>
public sealed class JevGenTelemetryOptions
{
    /// <summary>Whether to record the confidence of returned answers. On by default.</summary>
    public bool RecordConfidence { get; set; } = true;

    /// <summary>Whether to record question identifiers. On by default: identifiers are contract shape, not data.</summary>
    public bool RecordQuestionNames { get; set; } = true;

    /// <summary>Whether to record question prompts. Off by default: prompts can embed business rules.</summary>
    public bool RecordPrompts { get; set; }

    /// <summary>
    /// Whether to record the evaluation state. Off by default, and turning it on means
    /// accepting that model inputs will reach your telemetry backend.
    /// </summary>
    public bool RecordState { get; set; }

    /// <summary>Whether to record selected answers. Off by default.</summary>
    public bool RecordAnswers { get; set; }

    /// <summary>Whether to record provider request identifiers. On by default, for correlation.</summary>
    public bool RecordRequestIds { get; set; } = true;
}
