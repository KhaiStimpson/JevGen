using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JevGen.Generator;

/// <summary>
/// The JevGen incremental source generator.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is deliberately shallow: a syntax predicate narrows to attributed interfaces,
/// a semantic transform turns each one into an immutable, value-equal contract model, and the
/// emitters turn that model into source. Nothing downstream of the model looks at a symbol, so
/// editing one contract never regenerates the others.
/// </para>
/// <para>
/// Diagnostics travel with the model rather than being reported from the transform, because
/// transform output is cached and reported diagnostics would otherwise be dropped on a cache hit.
/// </para>
/// </remarks>
/// <summary>Names for the pipeline steps, so tests can assert that caching actually holds.</summary>
internal static class TrackingNames
{
    internal const string Contracts = "JevContracts";
    internal const string JsonContexts = "JevJsonContexts";
}

[Generator(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class JevClientGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contracts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownNames.JevClientAttribute,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (syntaxContext, cancellationToken) => Transform(syntaxContext, cancellationToken))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!)
            .WithTrackingName(TrackingNames.Contracts);

        context.RegisterSourceOutput(contracts, static (productionContext, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (result.Client is { } client)
            {
                productionContext.AddSource(client.HintName, client.Source);
            }
        });

        // One serializer-context bootstrap per consuming assembly, per the design's
        // "one generated JSON context per consuming assembly" decision.
        var jsonContexts = context.CompilationProvider
            .Select(static (compilation, _) => JsonContextModel.From(compilation))
            .WithTrackingName(TrackingNames.JsonContexts);

        context.RegisterSourceOutput(jsonContexts, static (productionContext, model) =>
        {
            if (model.ContextTypeNames.Length > 0)
            {
                productionContext.AddSource("JevGenAssemblyJson.g.cs", JsonBootstrapEmitter.Emit(model));
            }
        });
    }

    private static TransformResult? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol contract)
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var parser = new ContractParser(diagnostics.Add);

        var model = parser.Parse(contract, context.Attributes[0], cancellationToken);

        return new TransformResult(
            model is null ? null : new GeneratedClient(model.HintName, ClientEmitter.Emit(model)),
            new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
    }

    internal sealed record TransformResult(GeneratedClient? Client, EquatableArray<DiagnosticInfo> Diagnostics);

    internal sealed record GeneratedClient(string HintName, string Source);
}
