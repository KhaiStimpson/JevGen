using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace JevGen.Analyzers;

/// <summary>
/// Reports the contract problems that only become visible across a whole compilation, which
/// the per-contract source generator is not in a position to see.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JevContractAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Generator.JevDiagnostics.MissingJevClientAttribute,
            Generator.JevDiagnostics.MissingSerializerContext);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationStart =>
        {
            var state = new CompilationState();

            compilationStart.RegisterSymbolAction(
                symbolContext => AnalyzeInterface(symbolContext, state),
                SymbolKind.NamedType);

            compilationStart.RegisterCompilationEndAction(
                endContext => ReportSerializerContext(endContext, state));
        });
    }

    private sealed class CompilationState
    {
        private int _declaresClient;

        internal void MarkClientDeclared() => Interlocked.Exchange(ref _declaresClient, 1);

        internal bool DeclaresClient => Volatile.Read(ref _declaresClient) == 1;
    }

    private static void AnalyzeInterface(SymbolAnalysisContext context, CompilationState state)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Interface } contract)
        {
            return;
        }

        if (HasAttribute(contract, Generator.KnownNames.JevClientAttribute))
        {
            state.MarkClientDeclared();
            return;
        }

        var declaresQuestions = contract.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(method => method.GetAttributes().Any(attribute =>
                IsAttribute(attribute, Generator.KnownNames.JevNoulAttribute)
                || IsAttribute(attribute, Generator.KnownNames.JevChoiceAttribute)
                || IsAttribute(attribute, Generator.KnownNames.JevScoreAttribute)
                || IsAttribute(attribute, Generator.KnownNames.JevEvaluateAttribute)));

        if (!declaresQuestions)
        {
            return;
        }

        var location = contract.Locations.FirstOrDefault(candidate => candidate.IsInSource);

        if (location is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Generator.JevDiagnostics.MissingJevClientAttribute,
                location,
                contract.Name));
        }
    }

    private static void ReportSerializerContext(CompilationAnalysisContext context, CompilationState state)
    {
        if (!state.DeclaresClient)
        {
            return;
        }

        var assembly = context.Compilation.Assembly;

        if (assembly.GetAttributes().Any(attribute => IsAttribute(attribute, Generator.KnownNames.JevJsonContextAttribute)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Generator.JevDiagnostics.MissingSerializerContext,
            Location.None));
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().Any(attribute => IsAttribute(attribute, metadataName));

    private static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        if (attribute.AttributeClass is not { } attributeClass)
        {
            return false;
        }

        var name = attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return (name.StartsWith("global::", System.StringComparison.Ordinal)
            ? name.Substring("global::".Length)
            : name) == metadataName;
    }
}
