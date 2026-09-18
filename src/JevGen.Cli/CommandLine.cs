using System.Reflection;

namespace JevGen.Cli;

/// <summary>
/// The <c>jevgen</c> command-line tool.
/// </summary>
/// <remarks>
/// Every command reads the compile-time metadata the source generator emitted into an assembly.
/// Nothing here needs credentials or a network, which is what makes <c>inspect</c> and
/// <c>validate</c> safe to run in CI on any build output.
/// </remarks>
public static class CommandLine
{
    /// <summary>Runs the tool.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            WriteUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] is "-v" or "--version")
        {
            Console.WriteLine(Version());
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "inspect" => await Commands.InspectAsync(args[1..]).ConfigureAwait(false),
                "validate" => await Commands.ValidateAsync(args[1..]).ConfigureAwait(false),
                "doctor" => Commands.Doctor(args[1..]),
                _ => Unknown(args[0]),
            };
        }
        catch (JevGenException exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            return 1;
        }
        catch (FileNotFoundException exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            return 1;
        }
        catch (BadImageFormatException exception)
        {
            Console.Error.WriteLine($"error: '{exception.FileName}' is not a managed assembly.");
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'.");
        Console.Error.WriteLine();
        WriteUsage(Console.Error);
        return 1;
    }

    private static string Version()
        => typeof(CommandLine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(CommandLine).Assembly.GetName().Version?.ToString()
           ?? "unknown";

    private static void WriteUsage(TextWriter? writer = null)
    {
        writer ??= Console.Out;

        writer.WriteLine($"jevgen {Version()}");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  jevgen inspect <assembly> [--json]     Show the contracts an assembly declares.");
        writer.WriteLine("  jevgen validate <assembly>             Check every contract is well formed.");
        writer.WriteLine("  jevgen doctor                          Check the local JevGen environment.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -h, --help                             Show this help.");
        writer.WriteLine("  -v, --version                          Show the tool version.");
        writer.WriteLine();
        writer.WriteLine("These commands read compile-time metadata only. None of them contacts a");
        writer.WriteLine("provider or needs credentials.");
    }
}
