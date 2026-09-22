using Microsoft.CodeAnalysis;
using Xunit;

namespace JevGen.Generator.Tests;

/// <summary>
/// Every invalid contract must fail at compile time with a specific, actionable diagnostic
/// rather than generating code that will not build or, worse, code that quietly misbehaves.
/// </summary>
public sealed class DiagnosticTests
{
    private static IEnumerable<string> Run(string body)
        => GeneratorHarness.Run(TestContracts.Wrap(body)).GeneratorDiagnostics.Select(d => d.Id);

    [Fact]
    public void JEV002_UnsupportedReturnType()
        => Assert.Contains("JEV002", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevChoice("Route it.")]
                Task<string> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV002_NonAsyncReturnType()
        => Assert.Contains("JEV002", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevChoice("Route it.")]
                ChoiceResult<Department> Route(Ticket ticket);
            }
            """));

    [Fact]
    public void JEV002_PrimitiveReturnRequiresOptIn()
        => Assert.Contains("JEV002", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<bool> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void PrimitiveReturnIsAllowedWhenOptedInto()
    {
        var result = GeneratorHarness.Run(TestContracts.Wrap("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?", AllowPrimitiveResult = true)]
                Task<bool> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Errors);
        Assert.Contains("Probability >= 0.5d", result.Source("ITicketAI"), StringComparison.Ordinal);
    }

    [Fact]
    public void JEV003_MissingStateParameter()
        => Assert.Contains("JEV003", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync(CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV004_MultipleStateParameters()
        => Assert.Contains("JEV004", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync([State] Ticket a, [State] Ticket b, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV004_UnmarkedParameterAlongsideExplicitState()
        => Assert.Contains("JEV004", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync([State] Ticket a, string stray, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV005_ChoiceTypeMustBeAnEnum()
        => Assert.Contains("JEV005", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevChoice("Route it.")]
                Task<ChoiceResult<Ticket>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Theory]
    [InlineData("Min = 5, Max = 1")]
    [InlineData("Min = 1")]
    [InlineData("")]
    public void JEV006_InvalidScoreDefinition(string scale)
    {
        var separator = scale.Length == 0 ? string.Empty : ", ";

        Assert.Contains("JEV006", Run($$"""
            [JevClient]
            public interface ITicketAI
            {
                [JevScore("Rate it."{{separator}}{{scale}})]
                Task<ScoreResult> RateAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));
    }

    [Fact]
    public void JEV007_MissingEnumOptionCriteria()
        => Assert.Contains("JEV007", GeneratorHarness.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using JevGen;

            namespace Support;

            public enum Department { Billing, Technical }

            public sealed record Ticket { public required string Subject { get; init; } }

            [JevClient]
            public interface ITicketAI
            {
                [JevChoice("Route it.")]
                Task<ChoiceResult<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """).GeneratorDiagnostics.Select(d => d.Id));

    [Fact]
    public void JEV008_DuplicateQuestionId()
        => Assert.Contains("JEV008", Run("""
            public sealed record Assessment
            {
                [JevNoul("First?", Id = "same")]
                public required NoulResult A { get; init; }

                [JevNoul("Second?", Id = "same")]
                public required NoulResult B { get; init; }
            }

            [JevClient]
            public interface ITicketAI
            {
                [JevEvaluate]
                Task<Assessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV009_UnsupportedPropertyResultType()
        => Assert.Contains("JEV009", Run("""
            public sealed record Assessment
            {
                [JevNoul("Is it urgent?")]
                public required NoulResult Urgent { get; init; }

                public required string Notes { get; init; }
            }

            [JevClient]
            public interface ITicketAI
            {
                [JevEvaluate]
                Task<Assessment> AssessAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV010_DuplicateCancellationToken()
        => Assert.Contains("JEV010", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken a, CancellationToken b);
            }
            """));

    [Fact]
    public void JEV011_CancellationTokenShouldBeLast()
        => Assert.Contains("JEV011", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync(CancellationToken cancellationToken, Ticket ticket);
            }
            """));

    [Fact]
    public void JEV012_StateTypeCannotBeSerialized()
        => Assert.Contains("JEV012", Run("""
            public interface IState { }

            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync(IState state, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV013_UnsupportedGenericClient()
        => Assert.Contains("JEV013", Run("""
            [JevClient]
            public interface ITicketAI<T>
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV014_QuestionAttributeMissing()
        => Assert.Contains("JEV014", Run("""
            [JevClient]
            public interface ITicketAI
            {
                Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV015_DuplicateQuestionAttributes()
        => Assert.Contains("JEV015", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                [JevChoice("Route it.")]
                Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Theory]
    [InlineData("AcceptAbove = 1.4, ReviewAbove = 0.6")]
    [InlineData("AcceptAbove = 0.5, ReviewAbove = 0.9")]
    public void JEV016_InvalidConfidenceThreshold(string thresholds)
        => Assert.Contains("JEV016", Run($$"""
            [JevClient]
            public interface ITicketAI
            {
                [DecisionPolicy({{thresholds}})]
                [JevChoice("Route it.")]
                Task<Decision<Department>> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV017_ShapeCannotCarryRequiredProbabilities()
        => Assert.Contains("JEV017", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevChoice("Route it.", AllowPrimitiveResult = true, RequireProbabilities = true)]
                Task<Department> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

    [Fact]
    public void JEV019_MissingCancellationToken()
        => Assert.Contains("JEV019", Run("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync(Ticket ticket);
            }
            """));

    [Theory]
    [InlineData("""
        [JevClient]
        public interface ITicketAI
        {
            [JevNoul("Is it urgent?")]
            Task<NoulResult> CheckAsync(Ticket ticket, CancellationToken cancellationToken = default);

            [JevNoul("Is it spam?")]
            Task<NoulResult> CheckAsync(Ticket ticket, [Context("channel")] string channel, CancellationToken cancellationToken = default);
        }
        """)]
    [InlineData("""
        public interface IBase
        {
            [JevNoul("Is it spam?")]
            Task<NoulResult> SpamAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }

        [JevClient]
        public interface ITicketAI : IBase
        {
            [JevNoul("Is it urgent?")]
            Task<NoulResult> CheckAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """)]
    [InlineData("""
        [JevClient]
        public interface ITicketAI
        {
            string Name { get; }

            [JevNoul("Is it urgent?")]
            Task<NoulResult> CheckAsync(Ticket ticket, CancellationToken cancellationToken = default);
        }
        """)]
    public void JEV021_UnsupportedContractMember(string body)
    {
        var result = GeneratorHarness.Run(TestContracts.Wrap(body));

        Assert.Contains("JEV021", result.GeneratorDiagnostics.Select(d => d.Id));

        // Reported instead of, not on top of, "does not implement interface member".
        Assert.DoesNotContain(result.Sources.Keys, key => key.Contains("ITicketAI", StringComparison.Ordinal));
        Assert.DoesNotContain("CS0535", result.Errors.Select(d => d.Id));
    }

    [Fact]
    public void DefaultInterfaceMethodsAreLeftAlone()
    {
        var result = GeneratorHarness.Run(TestContracts.Wrap("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);

                async Task<bool> IsDefinitelyUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default)
                    => (await IsUrgentAsync(ticket, cancellationToken)).Value(0.9);
            }
            """));

        Assert.DoesNotContain("JEV021", result.GeneratorDiagnostics.Select(d => d.Id));
        Assert.DoesNotContain("JEV014", result.GeneratorDiagnostics.Select(d => d.Id));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void OneInvalidMethodSuppressesTheWholeClient()
    {
        var result = GeneratorHarness.Run(TestContracts.Wrap("""
            [JevClient]
            public interface ITicketAI
            {
                [JevNoul("Is it urgent?")]
                Task<NoulResult> IsUrgentAsync(Ticket ticket, CancellationToken cancellationToken = default);

                [JevChoice("Route it.")]
                Task<string> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

        Assert.Contains("JEV002", result.GeneratorDiagnostics.Select(d => d.Id));
        Assert.DoesNotContain("CS0535", result.Errors.Select(d => d.Id));
    }

    [Fact]
    public void InvalidContractsDoNotEmitBrokenCode()
    {
        var result = GeneratorHarness.Run(TestContracts.Wrap("""
            [JevClient]
            public interface ITicketAI
            {
                [JevChoice("Route it.")]
                Task<string> RouteAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

        // A contract that cannot be mapped produces diagnostics and no source, never source
        // that fails to compile on top of the original error.
        Assert.Contains("JEV002", result.GeneratorDiagnostics.Select(d => d.Id));
        Assert.Empty(result.Sources);
    }
}
