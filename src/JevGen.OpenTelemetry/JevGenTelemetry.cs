using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace JevGen.Telemetry;

/// <summary>The activity sources and instruments JevGen emits.</summary>
public static class JevGenTelemetry
{
    /// <summary>The name of the activity source and meter.</summary>
    public const string Name = "JevGen";

    /// <summary>The instrumentation version reported to collectors.</summary>
    public static string Version { get; } =
        typeof(JevGenTelemetry).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>The activity source for evaluations, provider calls, policies and fallback.</summary>
    public static ActivitySource ActivitySource { get; } = new(Name, Version);

    /// <summary>The meter carrying JevGen's instruments.</summary>
    public static Meter Meter { get; } = new(Name, Version);

    /// <summary>Activity names.</summary>
    public static class Activities
    {
        /// <summary>One end-to-end evaluation, spanning retries and fallbacks.</summary>
        public const string Evaluate = "JevGen.Evaluate";

        /// <summary>One call to one provider.</summary>
        public const string ProviderCall = "JevGen.ProviderCall";

        /// <summary>One policy classification.</summary>
        public const string Policy = "JevGen.Policy";

        /// <summary>One fallback to a secondary provider.</summary>
        public const string Fallback = "JevGen.Fallback";
    }

    internal static readonly Counter<long> Requests =
        Meter.CreateCounter<long>("jevgen.requests", "{request}", "Evaluations started.");

    internal static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("jevgen.request.duration", "s", "Evaluation duration.");

    internal static readonly Counter<long> Errors =
        Meter.CreateCounter<long>("jevgen.errors", "{error}", "Evaluations that failed.");

    internal static readonly Histogram<double> Confidence =
        Meter.CreateHistogram<double>("jevgen.confidence", "{probability}", "Confidence of returned answers.");

    internal static readonly Histogram<int> QuestionCount =
        Meter.CreateHistogram<int>("jevgen.questions", "{question}", "Questions per evaluation.");

    internal static readonly Counter<long> Retries =
        Meter.CreateCounter<long>("jevgen.retries", "{attempt}", "Provider attempts beyond the first.");

    internal static readonly Counter<long> Fallbacks =
        Meter.CreateCounter<long>("jevgen.fallbacks", "{fallback}", "Fallbacks to a secondary provider.");

    internal static readonly Counter<long> PolicyAccept =
        Meter.CreateCounter<long>("jevgen.policy.accept", "{decision}", "Decisions classified as accepted.");

    internal static readonly Counter<long> PolicyReview =
        Meter.CreateCounter<long>("jevgen.policy.review", "{decision}", "Decisions classified as needing review.");

    internal static readonly Counter<long> PolicyReject =
        Meter.CreateCounter<long>("jevgen.policy.reject", "{decision}", "Decisions classified as rejected.");

    /// <summary>Records how a policy classified a decision.</summary>
    public static void RecordPolicy(DecisionAction action, string contract, string method)
    {
        var tags = new TagList
        {
            { "jevgen.contract", contract },
            { "jevgen.method", method },
        };

        switch (action)
        {
            case DecisionAction.Accept: PolicyAccept.Add(1, tags); break;
            case DecisionAction.Review: PolicyReview.Add(1, tags); break;
            case DecisionAction.Reject: PolicyReject.Add(1, tags); break;
        }
    }
}
