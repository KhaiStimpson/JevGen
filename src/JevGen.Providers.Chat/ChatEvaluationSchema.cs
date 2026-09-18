using System.Text;
using System.Text.Json;
using JevGen.Providers;

namespace JevGen.Providers.Chat;

/// <summary>
/// Expresses Jev questions as a JSON schema and instruction text a general-purpose chat model
/// can answer.
/// </summary>
/// <remarks>
/// <para>
/// Jev is not a chat model, and this translation does not pretend otherwise. A chat model
/// asked for structured output can approximate a noul, a choice and a score, but its
/// self-reported probabilities are not calibrated the way a purpose-built evaluation model's
/// are. Providers built on this declare <see cref="JevProviderCapabilities.Probabilities"/>
/// only when the caller opts in, so a contract that depends on calibrated distributions fails
/// loudly here instead of quietly receiving worse numbers.
/// </para>
/// </remarks>
public static class ChatEvaluationSchema
{
    /// <summary>The system instruction that frames the evaluation task.</summary>
    public const string SystemInstruction =
        "You are an evaluation engine. You are given a state object and a set of questions about it. " +
        "Answer every question using only the state. Reply with a single JSON object matching the " +
        "requested schema, and nothing else. Report probabilities as numbers between 0 and 1, and " +
        "make them reflect genuine uncertainty rather than defaulting to confident values.";

    /// <summary>Builds the user message: the state, then the questions.</summary>
    public static string BuildPrompt(JevProviderRequest request)
    {
        var builder = new StringBuilder();

        builder.AppendLine("State:");
        builder.AppendLine(request.SerializeState(JevGenJson.Options).GetRawText());
        builder.AppendLine();
        builder.AppendLine("Questions:");

        foreach (var question in request.Questions)
        {
            builder.Append("- \"").Append(question.Id).Append("\" (")
                .Append(question.Kind.ToString().ToLowerInvariant()).Append("): ")
                .AppendLine(question.Prompt);

            switch (question.Kind)
            {
                case JevQuestionKind.Choice:
                    builder.AppendLine("  Options:");

                    foreach (var option in question.Options)
                    {
                        builder.Append("    - ").Append(option.Id);

                        if (option.Criteria is { Length: > 0 } criteria)
                        {
                            builder.Append(": ").Append(criteria);
                        }

                        builder.AppendLine();
                    }

                    if (question.RequiresProbabilities)
                    {
                        builder.AppendLine("  Report a probability for every option; they must sum to 1.");
                    }

                    break;

                case JevQuestionKind.Score:
                    builder.Append("  Scale: ")
                        .Append(question.Minimum ?? 0d)
                        .Append(" to ")
                        .Append(question.Maximum ?? 1d)
                        .AppendLine(" inclusive.");

                    if (question.Criteria.Length > 0)
                    {
                        builder.Append("  Rubric, lowest to highest: ")
                            .AppendLine(string.Join(", ", question.Criteria));
                    }

                    break;

                case JevQuestionKind.Noul:
                    builder.AppendLine("  Report the probability that the statement is true.");
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Builds the JSON schema the model must match, as a serialized schema document.</summary>
    public static string BuildSchema(JevProviderRequest request)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteBoolean("additionalProperties", false);

            writer.WriteStartArray("required");
            writer.WriteStringValue("answers");
            writer.WriteEndArray();

            writer.WriteStartObject("properties");
            writer.WriteStartObject("answers");
            writer.WriteString("type", "object");
            writer.WriteBoolean("additionalProperties", false);

            writer.WriteStartArray("required");

            foreach (var question in request.Questions)
            {
                writer.WriteStringValue(question.Id);
            }

            writer.WriteEndArray();
            writer.WriteStartObject("properties");

            foreach (var question in request.Questions)
            {
                WriteQuestionSchema(writer, question);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteQuestionSchema(Utf8JsonWriter writer, JevQuestionDefinition question)
    {
        writer.WriteStartObject(question.Id);
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);

        switch (question.Kind)
        {
            case JevQuestionKind.Noul:
                WriteRequired(writer, "probability");
                writer.WriteStartObject("properties");
                WriteNumber(writer, "probability", 0d, 1d);
                writer.WriteEndObject();
                break;

            case JevQuestionKind.Choice:
                if (question.RequiresProbabilities)
                {
                    WriteRequired(writer, "choice", "probabilities");
                }
                else
                {
                    WriteRequired(writer, "choice");
                }

                writer.WriteStartObject("properties");

                writer.WriteStartObject("choice");
                writer.WriteString("type", "string");
                writer.WriteStartArray("enum");

                foreach (var option in question.Options)
                {
                    writer.WriteStringValue(option.Id);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();

                writer.WriteStartObject("probabilities");
                writer.WriteString("type", "object");
                writer.WriteBoolean("additionalProperties", false);
                writer.WriteStartArray("required");

                foreach (var option in question.Options)
                {
                    writer.WriteStringValue(option.Id);
                }

                writer.WriteEndArray();
                writer.WriteStartObject("properties");

                foreach (var option in question.Options)
                {
                    WriteNumber(writer, option.Id, 0d, 1d);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
                break;

            case JevQuestionKind.Score:
                WriteRequired(writer, "score");
                writer.WriteStartObject("properties");
                WriteNumber(writer, "score", question.Minimum ?? 0d, question.Maximum ?? 1d);
                WriteNumber(writer, "confidence", 0d, 1d);
                writer.WriteEndObject();
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteRequired(Utf8JsonWriter writer, params string[] names)
    {
        writer.WriteStartArray("required");

        foreach (var name in names)
        {
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, double minimum, double maximum)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "number");
        writer.WriteNumber("minimum", minimum);
        writer.WriteNumber("maximum", maximum);
        writer.WriteEndObject();
    }
}
