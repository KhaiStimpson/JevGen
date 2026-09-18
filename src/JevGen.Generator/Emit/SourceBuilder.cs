using System;
using System.Globalization;
using System.Text;

namespace JevGen.Generator;

/// <summary>A small indentation-aware writer, so generated code stays readable and inspectable.</summary>
internal sealed class SourceBuilder
{
    private readonly StringBuilder _builder = new();
    private int _indent;

    internal SourceBuilder AppendLine()
    {
        _builder.Append('\n');
        return this;
    }

    internal SourceBuilder AppendLine(string text)
    {
        if (text.Length > 0)
        {
            _builder.Append(' ', _indent * 4).Append(text);
        }

        _builder.Append('\n');
        return this;
    }

    /// <summary>Rewrites the trailing occurrence of <paramref name="from"/>, used to close initializers.</summary>
    internal void Replace(string from, string to)
    {
        var index = _builder.ToString().LastIndexOf(from, System.StringComparison.Ordinal);

        if (index >= 0)
        {
            _builder.Remove(index, from.Length).Insert(index, to);
        }
    }

    internal SourceBuilder Open(string text)
    {
        AppendLine(text);
        AppendLine("{");
        _indent++;
        return this;
    }

    internal SourceBuilder Close(string suffix = "")
    {
        _indent--;
        AppendLine("}" + suffix);
        return this;
    }

    internal IDisposable Block(string text)
    {
        Open(text);
        return new Closer(this);
    }

    /// <summary>Opens a bare brace block, for object and collection initializers.</summary>
    internal IDisposable Braces()
    {
        AppendLine("{");
        _indent++;
        return new Closer(this);
    }

    public override string ToString() => _builder.ToString();

    /// <summary>Renders a C# string literal.</summary>
    internal static string Literal(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                case '\0': builder.Append("\\0"); break;
                default:
                    if (character < ' ')
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>Renders a <see cref="double"/> literal that round-trips.</summary>
    internal static string Number(double value)
        => value.ToString("R", CultureInfo.InvariantCulture) + "d";

    /// <summary>Turns an arbitrary question identifier into a valid C# identifier fragment.</summary>
    internal static string Identifier(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    private sealed class Closer(SourceBuilder builder) : IDisposable
    {
        public void Dispose() => builder.Close();
    }
}
