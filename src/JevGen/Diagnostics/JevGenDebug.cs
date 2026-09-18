using System.Text.Json;
using JevGen.Providers;

namespace JevGen;

/// <summary>
/// Inspects what a contract will send, without sending it.
/// </summary>
/// <remarks>
/// Everything here reads compile-time metadata captured by the source generator, so it needs no
/// credentials, no provider and no network access.
/// </remarks>
public static class JevGenDebug
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    /// <summary>Describes a contract as indented JSON.</summary>
    /// <exception cref="JevGenException">The contract has no generated client.</exception>
    public static string Describe<TContract>()
        where TContract : class
        => Describe(JevClientRegistry.Get<TContract>());

    /// <summary>Describes a contract as indented JSON.</summary>
    /// <exception cref="JevGenException">The contract has no generated client.</exception>
    public static string Describe(Type contractType)
        => Describe(JevClientRegistry.Get(contractType));

    /// <summary>Describes every registered contract as indented JSON.</summary>
    public static string DescribeAll()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartArray();

            foreach (var descriptor in JevClientRegistry.All.OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                WriteClient(writer, descriptor);
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Describes a contract as indented JSON.</summary>
    public static string Describe(JevClientDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteClient(writer, descriptor);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Renders a contract as the aligned text summary the CLI's <c>inspect</c> command prints.
    /// </summary>
    public static string Summarize(JevClientDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var builder = new System.Text.StringBuilder();

        foreach (var method in descriptor.Methods)
        {
            builder.Append(descriptor.Name).Append('.').AppendLine(method.Name);
            builder.Append("  State: ").AppendLine(method.StateTypeName);

            if (method.Questions.Length == 0)
            {
                continue;
            }

            builder.AppendLine("  Questions:");

            var width = method.Questions.Max(q => q.Id.Length);

            foreach (var question in method.Questions)
            {
                builder
                    .Append("    ")
                    .Append(question.Id.PadRight(width + 2))
                    .AppendLine(DescribeKind(question));
            }
        }

        return builder.ToString();
    }

    private static string DescribeKind(JevQuestionDefinition question) => question.Kind switch
    {
        JevQuestionKind.Choice when question.Options.Length > 0
            => $"Choice<{string.Join(" | ", question.Options.Select(o => o.Id))}>",
        JevQuestionKind.Choice => "Choice",
        JevQuestionKind.Score when question.Minimum is { } min && question.Maximum is { } max
            => $"Score [{min:0.##}..{max:0.##}]",
        _ => question.Kind.ToString(),
    };

    private static void WriteClient(Utf8JsonWriter writer, JevClientDescriptor descriptor)
    {
        writer.WriteStartObject();
        writer.WriteString("client", descriptor.Name);
        writer.WriteString("contractType", descriptor.ContractType.FullName);

        if (descriptor.ContractVersion is not null)
        {
            writer.WriteString("version", descriptor.ContractVersion);
        }

        if (descriptor.Provider is not null)
        {
            writer.WriteString("provider", descriptor.Provider);
        }

        if (descriptor.Model is not null)
        {
            writer.WriteString("model", descriptor.Model);
        }

        writer.WriteStartArray("methods");

        foreach (var method in descriptor.Methods)
        {
            writer.WriteStartObject();
            writer.WriteString("method", method.Name);
            writer.WriteString("state", method.StateTypeName);
            writer.WriteString("result", method.ResultTypeName);
            writer.WriteString("requires", method.RequiredCapabilities.ToString());
            writer.WriteStartArray("questions");

            foreach (var question in method.Questions)
            {
                WriteQuestion(writer, question);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteQuestion(Utf8JsonWriter writer, JevQuestionDefinition question)
    {
        writer.WriteStartObject();
        writer.WriteString("id", question.Id);
        writer.WriteString("type", question.Kind.ToString().ToLowerInvariant());
        writer.WriteString("prompt", question.Prompt);

        if (question.Options.Length > 0)
        {
            writer.WriteStartArray("options");

            foreach (var option in question.Options)
            {
                writer.WriteStartObject();
                writer.WriteString("id", option.Id);

                if (option.Criteria is not null)
                {
                    writer.WriteString("criteria", option.Criteria);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (question.Minimum is { } minimum)
        {
            writer.WriteNumber("min", minimum);
        }

        if (question.Maximum is { } maximum)
        {
            writer.WriteNumber("max", maximum);
        }

        if (question.Criteria.Length > 0)
        {
            writer.WriteStartArray("criteria");

            foreach (var criterion in question.Criteria)
            {
                writer.WriteStringValue(criterion);
            }

            writer.WriteEndArray();
        }

        if (question.Model is not null)
        {
            writer.WriteString("model", question.Model);
        }

        if (question.Provider is not null)
        {
            writer.WriteString("provider", question.Provider);
        }

        writer.WriteEndObject();
    }
}
