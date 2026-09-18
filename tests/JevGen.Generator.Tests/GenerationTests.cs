using Microsoft.CodeAnalysis;
using Xunit;

namespace JevGen.Generator.Tests;

/// <summary>
/// Verifies that valid contracts generate code that compiles, and that the generated code says
/// what it should.
/// </summary>
public sealed class GenerationTests
{
    [Theory]
    [InlineData(nameof(TestContracts.Choice))]
    [InlineData(nameof(TestContracts.Noul))]
    [InlineData(nameof(TestContracts.Score))]
    [InlineData(nameof(TestContracts.ScoreCriteria))]
    [InlineData(nameof(TestContracts.Aggregate))]
    [InlineData(nameof(TestContracts.NestedAggregate))]
    [InlineData(nameof(TestContracts.CompositeState))]
    [InlineData(nameof(TestContracts.Decision))]
    [InlineData(nameof(TestContracts.ProviderOptions))]
    public void ValidContractsGenerateCompilableCode(string contractName)
    {
        var body = (string)typeof(TestContracts)
            .GetField(contractName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;

        var result = GeneratorHarness.Run(TestContracts.Wrap(body));

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Sources);
    }

    [Fact]
    public void ChoiceGeneratesCachedQuestionMetadataAndOptionMapping()
    {
        var result = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.Choice));
        var source = result.Source("ITicketAI");

        // Question metadata is built once into a static field, never per call.
        Assert.Contains("static readonly", source, StringComparison.Ordinal);
        Assert.Contains("RouteAsync_Questions", source, StringComparison.Ordinal);
        Assert.Contains("Id = \"route\"", source, StringComparison.Ordinal);
        Assert.Contains("JevQuestionKind.Choice", source, StringComparison.Ordinal);
        Assert.Contains("RequiresProbabilities = true", source, StringComparison.Ordinal);

        // Enum options carry their declared identifiers and criteria.
        Assert.Contains("JevChoiceOption(\"billing\", \"Invoices, payments and refunds\")", source, StringComparison.Ordinal);
        Assert.Contains("case \"technical\":", source, StringComparison.Ordinal);
        Assert.Contains("return global::Support.Department.Technical;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedClientTargetsTheRuntimeAbstractionOnly()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.Choice)).Source("ITicketAI");

        Assert.Contains("global::JevGen.IEvaluationRuntime _runtime", source, StringComparison.Ordinal);

        // No provider, HTTP or serializer type may appear in a generated client.
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JevGen.Providers", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedCodeUsesNoReflection()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.Aggregate)).Source("ITicketAI");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMethod(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Expression.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AggregateIssuesEveryQuestionInOneRequest()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.Aggregate)).Source("ITicketAI");

        Assert.Contains("Id = \"urgent\"", source, StringComparison.Ordinal);
        Assert.Contains("Id = \"department\"", source, StringComparison.Ordinal);
        Assert.Contains("Id = \"severity\"", source, StringComparison.Ordinal);

        // One request carries all three: a single AssessAsync_Questions array.
        Assert.Single(
            source.Split("AssessAsync_Questions =", StringSplitOptions.None).Skip(1));
        Assert.Contains("ContractVersion = \"2\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedAggregatePrefixesQuestionIdentifiers()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.NestedAggregate)).Source("ITicketAI");

        Assert.Contains("Id = \"risk.churn\"", source, StringComparison.Ordinal);
        Assert.Contains("Id = \"risk.exposure\"", source, StringComparison.Ordinal);
        Assert.Contains("Risk = new global::Support.RiskBreakdown", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScoreCriteriaImplyAOneBasedScale()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.ScoreCriteria)).Source("ITicketAI");

        Assert.Contains("Minimum = 1d", source, StringComparison.Ordinal);
        Assert.Contains("Maximum = 4d", source, StringComparison.Ordinal);
        Assert.Contains("Criteria = global::System.Collections.Immutable.ImmutableArray.Create<string>(\"Low\", \"Medium\", \"High\", \"Critical\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeStateSerializesEachPartWithItsOwnMetadata()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.CompositeState)).Source("ITicketAI");

        Assert.Contains("state[\"ticket\"] = global::JevGen.JevCompositeState.Part<global::Support.Ticket>(ticket);", source, StringComparison.Ordinal);
        Assert.Contains("state[\"region\"]", source, StringComparison.Ordinal);
        Assert.Contains("state[\"customerTier\"]", source, StringComparison.Ordinal);
        Assert.Contains("StateTypeInfo = global::JevGen.JevCompositeState.TypeInfo", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionClassifiesButDoesNotAct()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.Decision)).Source("ITicketAI");

        Assert.Contains("global::JevGen.DecisionAction.Accept", source, StringComparison.Ordinal);
        Assert.Contains("global::JevGen.DecisionAction.Review", source, StringComparison.Ordinal);
        Assert.Contains("global::JevGen.DecisionAction.Reject", source, StringComparison.Ordinal);
        Assert.Contains("answer.Confidence >= 0.9d", source, StringComparison.Ordinal);
        Assert.Contains("answer.Confidence >= 0.65d", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodProviderOverridesTakePrecedenceOverTheContract()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.ProviderOptions)).Source("ITicketAI");

        Assert.Contains("Provider = \"internal\"", source, StringComparison.Ordinal);
        Assert.Contains("Model = \"jev-latest\"", source, StringComparison.Ordinal);
        Assert.Contains("byProvider[\"openrouter\"]", source, StringComparison.Ordinal);
        Assert.Contains("[\"providerOrder\"] = \"typesafe\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientsRegisterThemselvesFromAModuleInitializer()
    {
        var source = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.Choice)).Source("ITicketAI");

        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("global::JevGen.JevClientRegistry.Register", source, StringComparison.Ordinal);

        // No assembly scanning anywhere.
        Assert.DoesNotContain("GetTypes()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Assembly.Load", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedTypesAreNamespacedToAvoidCollisions()
    {
        var first = GeneratorHarness.Run("""
            using System.Threading.Tasks;
            using JevGen;
            namespace A { public sealed record S { public string? X { get; init; } }
                [JevClient] public interface IDup { [JevNoul("q?")] Task<NoulResult> AskAsync(S s); } }
            namespace B { public sealed record S { public string? X { get; init; } }
                [JevClient] public interface IDup { [JevNoul("q?")] Task<NoulResult> AskAsync(S s); } }
            """);

        Assert.Empty(first.Errors);
        Assert.Equal(2, first.Sources.Count);
        Assert.Contains(first.Sources.Values, source => source.Contains("namespace JevGen.Generated.A", StringComparison.Ordinal));
        Assert.Contains(first.Sources.Values, source => source.Contains("namespace JevGen.Generated.B", StringComparison.Ordinal));
    }

    [Fact]
    public void QuestionIdentifiersMayRepeatAcrossMethods()
    {
        // Each method builds its own request, so two methods may legitimately ask the same
        // question id. Generated mappers must not collide.
        var result = GeneratorHarness.Run(TestContracts.Wrap("""
            [JevClient]
            public interface ITicketAI
            {
                [JevScore("Rate the severity.", Min = 1, Max = 5)]
                Task<ScoreResult> SeverityAsync(Ticket ticket, CancellationToken cancellationToken = default);

                [JevNoul("Is it urgent?", Id = "severity")]
                Task<NoulResult> AlsoSeverityAsync(Ticket ticket, CancellationToken cancellationToken = default);
            }
            """));

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void SensitiveStatePropertiesAreCapturedForRedaction()
    {
        var result = GeneratorHarness.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using JevGen;

            namespace Support;

            public sealed record Customer
            {
                [JevSensitive]
                public required string Email { get; init; }
            }

            public sealed record Transaction
            {
                public required decimal Amount { get; init; }

                [JevSensitive]
                public required string CardholderName { get; init; }

                public required Customer Customer { get; init; }
            }

            [JevClient]
            public interface IFraudAI
            {
                [JevNoul("Is this fraudulent?")]
                Task<NoulResult> AssessAsync(Transaction transaction, CancellationToken cancellationToken = default);
            }
            """);

        Assert.Empty(result.Errors);

        var source = result.Source("IFraudAI");

        // Captured at compile time so redaction needs no reflection, and nested state is covered.
        Assert.Contains("SensitiveProperties = global::System.Collections.Immutable.ImmutableArray.Create<string>(\"CardholderName\", \"Email\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var first = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.Aggregate)).Source("ITicketAI");
        var second = GeneratorHarness.Run(TestContracts.Wrap(TestContracts.Aggregate)).Source("ITicketAI");

        Assert.Equal(first, second);
    }

    [Fact]
    public void UnrelatedEditsDoNotRegenerate()
    {
        var uncached = GeneratorHarness.UncachedSteps(TestContracts.Wrap(TestContracts.Aggregate));

        Assert.True(
            uncached.Count == 0,
            "Editing an unrelated file invalidated the generator pipeline: " + string.Join(", ", uncached));
    }
}
