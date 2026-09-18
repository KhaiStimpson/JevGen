using System.Reflection;
using System.Runtime.Loader;

namespace JevGen.Cli;

/// <summary>The implementations behind the <c>jevgen</c> commands.</summary>
internal static class Commands
{
    internal static Task<int> InspectAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: inspect needs the path to a built assembly.");
            return Task.FromResult(1);
        }

        var json = args.Contains("--json", StringComparer.Ordinal);
        var descriptors = Load(args[0]);

        if (descriptors.Count == 0)
        {
            Console.WriteLine($"'{args[0]}' declares no JevGen contracts.");
            return Task.FromResult(0);
        }

        if (json)
        {
            Console.WriteLine(JevGenDebug.DescribeAll());
            return Task.FromResult(0);
        }

        foreach (var descriptor in descriptors.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            Console.Write(JevGenDebug.Summarize(descriptor));

            if (descriptor.ContractVersion is { } version)
            {
                Console.WriteLine($"  Version: {version}");
            }

            if (descriptor.Provider is { } provider)
            {
                Console.WriteLine($"  Provider: {provider}");
            }

            Console.WriteLine();
        }

        return Task.FromResult(0);
    }

    internal static Task<int> ValidateAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: validate needs the path to a built assembly.");
            return Task.FromResult(1);
        }

        var descriptors = Load(args[0]);

        if (descriptors.Count == 0)
        {
            Console.Error.WriteLine($"error: '{args[0]}' declares no JevGen contracts.");
            return Task.FromResult(1);
        }

        var problems = 0;

        foreach (var descriptor in descriptors.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            foreach (var method in descriptor.Methods)
            {
                var member = $"{descriptor.Name}.{method.Name}";

                if (method.Questions.Length == 0)
                {
                    Console.Error.WriteLine($"{member}: declares no questions.");
                    problems++;
                    continue;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var question in method.Questions)
                {
                    if (!seen.Add(question.Id))
                    {
                        Console.Error.WriteLine($"{member}: duplicate question id '{question.Id}'.");
                        problems++;
                    }

                    if (question.Kind == JevQuestionKind.Choice && question.Options.Length < 2)
                    {
                        Console.Error.WriteLine(
                            $"{member}: choice question '{question.Id}' offers fewer than two options.");
                        problems++;
                    }

                    if (question.Kind == JevQuestionKind.Score
                        && question.Minimum is { } min
                        && question.Maximum is { } max
                        && min >= max)
                    {
                        Console.Error.WriteLine(
                            $"{member}: score question '{question.Id}' has an empty range [{min}, {max}].");
                        problems++;
                    }
                }

                Console.WriteLine(
                    $"{member}: {method.Questions.Length} question(s), requires {method.RequiredCapabilities}.");
            }
        }

        if (problems > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"{problems} problem(s) found.");
            return Task.FromResult(1);
        }

        Console.WriteLine();
        Console.WriteLine($"{descriptors.Count} contract(s) validated.");
        return Task.FromResult(0);
    }

    internal static int Doctor(string[] args)
    {
        Console.WriteLine("JevGen environment");
        Console.WriteLine("------------------");
        Console.WriteLine($"Runtime:              {Environment.Version}");
        Console.WriteLine($"Framework:            {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS:                   {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Console.WriteLine($"Architecture:         {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"Reflection JSON:      {(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault ? "enabled" : "disabled (AOT or trimmed)")}");
        Console.WriteLine($"Registered contracts: {JevClientRegistry.All.Count}");

        if (args.Length > 0)
        {
            var descriptors = Load(args[0]);
            Console.WriteLine($"Contracts in '{Path.GetFileName(args[0])}': {descriptors.Count}");
        }

        if (!System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault)
        {
            Console.WriteLine();
            Console.WriteLine("Reflection-based serialization is off, so every state type needs a");
            Console.WriteLine("JsonSerializerContext named with [assembly: JevJsonContext(...)].");
        }

        return 0;
    }

    /// <summary>
    /// Loads an assembly and returns the contracts it registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generated clients register themselves from a module initializer, so loading the assembly
    /// is enough to populate the registry. Nothing is discovered by scanning types.
    /// </para>
    /// <para>
    /// The assembly is loaded into the default context on purpose. Loading it in isolation
    /// would give it its own copy of JevGen, and its clients would register into a registry
    /// this process cannot see.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<JevClientDescriptor> Load(string path)
    {
        var full = Path.GetFullPath(path);

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"The assembly '{full}' does not exist.", full);
        }

        var directory = Path.GetDirectoryName(full)!;

        if (Probing.Add(directory))
        {
            AssemblyLoadContext.Default.Resolving += ResolveNeighbour;
        }

        var before = JevClientRegistry.All.Select(descriptor => descriptor.ContractType).ToHashSet();

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(full);

        // Module initializers are lazy; running the module constructor forces registration.
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        return [.. JevClientRegistry.All.Where(descriptor => !before.Contains(descriptor.ContractType))];
    }

    private static readonly HashSet<string> Probing = new(StringComparer.Ordinal);

    private static Assembly? ResolveNeighbour(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (var directory in Probing)
        {
            var candidate = Path.Combine(directory, name.Name + ".dll");

            if (File.Exists(candidate))
            {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
