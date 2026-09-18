namespace JevGen.Generator.Tests;

/// <summary>Contract snippets shared by the generator tests.</summary>
internal static class TestContracts
{
    internal const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using JevGen;

        namespace Support;

        public enum Department
        {
            [JevOption("billing", "Invoices, payments and refunds")] Billing,
            [JevOption("technical", "Defects, outages and technical support")] Technical,
            [JevOption("sales", "Pricing and new business")] Sales,
        }

        public sealed record Ticket
        {
            public required string Subject { get; init; }
            public required string Body { get; init; }
        }

        """;

    internal static string Wrap(string body) => Preamble + body;

    internal const string Choice = """
        [JevClient]
        public interface ITicketAI
        {
            [JevChoice("Which department should handle this ticket?")]
            Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;

    internal const string Noul = """
        [JevClient]
        public interface ITicketAI
        {
            [JevNoul("Does this require urgent attention?")]
            Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;

    internal const string Score = """
        [JevClient]
        public interface ITicketAI
        {
            [JevScore("Rate the severity.", Min = 1, Max = 5)]
            ValueTask<ScoreResult> SeverityAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;

    internal const string ScoreCriteria = """
        [JevClient]
        public interface ITicketAI
        {
            [JevScore("Rate severity.", "Low", "Medium", "High", "Critical")]
            Task<ScoreResult> SeverityAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;

    internal const string Aggregate = """
        public sealed record TicketAssessment
        {
            [JevNoul("Does this require urgent attention?")]
            public required NoulResult Urgent { get; init; }

            [JevChoice("Which department should handle it?")]
            public required ChoiceResult<Department> Department { get; init; }

            [JevScore("Rate severity.", Min = 1, Max = 5)]
            public required ScoreResult Severity { get; init; }
        }

        [JevClient(Version = "2")]
        public interface ITicketAI
        {
            [JevEvaluate]
            Task<TicketAssessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;

    internal const string NestedAggregate = """
        public sealed record RiskBreakdown
        {
            [JevNoul("Is the customer likely to churn?")]
            public required NoulResult Churn { get; init; }

            [JevScore("Rate the financial exposure.", Min = 0, Max = 10)]
            public required ScoreResult Exposure { get; init; }
        }

        public sealed record TicketAssessment
        {
            [JevChoice("Which department should handle it?")]
            public required ChoiceResult<Department> Department { get; init; }

            public required RiskBreakdown Risk { get; init; }
        }

        [JevClient]
        public interface ITicketAI
        {
            [JevEvaluate]
            Task<TicketAssessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;

    internal const string CompositeState = """
        [JevClient]
        public interface ITicketAI
        {
            [JevChoice("Which department should handle this ticket?")]
            Task<ChoiceResult<Department>> RouteAsync(
                [State] Ticket ticket,
                [Context("region")] string region,
                [Context("customerTier")] int tier,
                CancellationToken cancellationToken = default);
        }
        """;

    internal const string Decision = """
        [JevClient]
        public interface ITicketAI
        {
            [DecisionPolicy(AcceptAbove = 0.9, ReviewAbove = 0.65)]
            [JevChoice("Which department should handle this ticket?")]
            Task<Decision<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;

    internal const string ProviderOptions = """
        [JevClient(Provider = "openrouter", Model = "jev-latest")]
        [JevProviderOption("openrouter", "providerOrder", "typesafe")]
        public interface ITicketAI
        {
            [JevChoice("Which department should handle this ticket?", Provider = "internal")]
            Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """;
}
