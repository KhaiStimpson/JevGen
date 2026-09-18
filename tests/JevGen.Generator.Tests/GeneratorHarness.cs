using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JevGen.Generator.Tests;

/// <summary>Compiles a snippet with the JevGen generator attached and captures what it produced.</summary>
internal static class GeneratorHarness
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    internal sealed record Result(
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompilationDiagnostics,
        IReadOnlyDictionary<string, string> Sources)
    {
        internal string Source(string hintNameFragment)
        {
            var match = Sources.FirstOrDefault(pair => pair.Key.Contains(hintNameFragment, StringComparison.Ordinal));

            return match.Value
                   ?? throw new InvalidOperationException(
                       $"No generated source matched '{hintNameFragment}'. Generated: " +
                       (Sources.Count == 0 ? "(nothing)" : string.Join(", ", Sources.Keys)));
        }

        internal IEnumerable<string> Ids(DiagnosticSeverity? minimum = null)
            => GeneratorDiagnostics
                .Concat(CompilationDiagnostics)
                .Where(diagnostic => minimum is null || diagnostic.Severity >= minimum)
                .Select(diagnostic => diagnostic.Id);

        /// <summary>Errors from compiling the snippet together with everything the generator emitted.</summary>
        internal ImmutableArray<Diagnostic> Errors
            => [.. CompilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];
    }

    internal static Result Run(string source, string assemblyName = "JevGen.Tests.Generated")
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Contract.cs");

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create([new JevClientGenerator().AsSourceGenerator()])
            .WithUpdatedParseOptions(parseOptions);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updated,
            out var generatorDiagnostics);

        var runResult = driver.GetRunResult();

        var sources = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .ToDictionary(
                generated => generated.HintName,
                generated => generated.SourceText.ToString(),
                StringComparer.Ordinal);

        return new Result(generatorDiagnostics, updated.GetDiagnostics(), sources);
    }

    /// <summary>Runs the generator twice and reports whether every step was served from cache.</summary>
    internal static IReadOnlyList<string> UncachedSteps(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Contract.cs");

        var compilation = CSharpCompilation.Create(
            "JevGen.Tests.Incremental",
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new JevClientGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        // A cosmetically different but semantically identical compilation must reuse every step.
        var second = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("// an unrelated edit", parseOptions, path: "Other.cs"));

        driver = driver.RunGenerators(second);

        // Roslyn's own Compilation and ForAttributeWithMetadataName join steps necessarily
        // re-run when any tree changes. What must hold is that JevGen's own steps do not:
        // that is what stops one contract's edit regenerating every other client.
        string[] ours = ["JevContracts", "JevJsonContexts"];

        return driver.GetRunResult().Results
            .SelectMany(result => result.TrackedSteps)
            .Where(step => ours.Contains(step.Key, StringComparer.Ordinal))
            .SelectMany(step => step.Value.SelectMany(s => s.Outputs.Select(o => (step.Key, o.Reason))))
            .Where(entry => entry.Reason is not (IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
            .Select(entry => entry.Key + ": " + entry.Reason)
            .Distinct()
            .ToList();
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");

        if (trusted is not null)
        {
            foreach (var path in trusted.Split(Path.PathSeparator))
            {
                if (path.Length > 0 && File.Exists(path))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        foreach (var assembly in new[] { typeof(JevClientAttribute).Assembly, typeof(ChoiceResult<>).Assembly })
        {
            AddAssembly(references, assembly);
        }

        return references.ToImmutable();
    }

    private static void AddAssembly(ImmutableArray<MetadataReference>.Builder references, Assembly assembly)
    {
        if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
        {
            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
    }
}
