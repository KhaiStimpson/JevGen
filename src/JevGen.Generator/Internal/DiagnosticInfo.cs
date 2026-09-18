using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace JevGen.Generator;

/// <summary>
/// A value-equal snapshot of a diagnostic, created before it is reported.
/// </summary>
/// <remarks>
/// A <see cref="Diagnostic"/> holds a <see cref="Location"/> that references a syntax tree,
/// which would root an entire compilation in the incremental generator's cache. Capturing the
/// file path and span instead keeps the pipeline's memory footprint flat across edits, and
/// keeps the model — diagnostics included — properly value-equal.
/// </remarks>
internal sealed record DiagnosticInfo : System.IEquatable<DiagnosticInfo>
{
    private DiagnosticInfo(
        DiagnosticDescriptor descriptor,
        string? filePath,
        TextSpan span,
        LinePositionSpan lineSpan,
        EquatableArray<string> arguments)
    {
        Descriptor = descriptor;
        FilePath = filePath;
        Span = span;
        LineSpan = lineSpan;
        Arguments = arguments;
    }

    internal DiagnosticDescriptor Descriptor { get; }

    internal string? FilePath { get; }

    internal TextSpan Span { get; }

    internal LinePositionSpan LineSpan { get; }

    internal EquatableArray<string> Arguments { get; }

    internal static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location location, params object?[] arguments)
    {
        var lineSpan = location.GetLineSpan();

        return new DiagnosticInfo(
            descriptor,
            location.IsInSource ? location.SourceTree?.FilePath : null,
            location.SourceSpan,
            lineSpan.Span,
            new EquatableArray<string>(
                arguments.Select(argument => argument?.ToString() ?? string.Empty).ToImmutableArray()));
    }

    internal Diagnostic ToDiagnostic()
    {
        var location = FilePath is null
            ? Location.None
            : Location.Create(FilePath, Span, LineSpan);

        return Diagnostic.Create(Descriptor, location, Arguments.Values.Cast<object?>().ToArray());
    }
}
