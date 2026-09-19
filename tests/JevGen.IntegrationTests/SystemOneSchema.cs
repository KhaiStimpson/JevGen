using System.Text.Json;

namespace JevGen.IntegrationTests;

/// <summary>
/// Validates a request against the System One schema TypeSafe publishes.
/// </summary>
/// <remarks>
/// <para>
/// The mock server runs every request through this before answering. Version 1.0.0-preview.1
/// shipped a wire format that no host accepts — <c>question</c> instead of <c>instructions</c>,
/// <c>options</c> instead of <c>criteria</c>, score bounds the schema does not define — and
/// nothing caught it, because the mock answered whatever it was sent. Rejecting a
/// non-conforming request here is what stops that happening again.
/// </para>
/// <para>
/// Transcribed from <c>https://api.typesafe.ai/openapi.json</c>, as vendored into
/// <c>typesafe_sdk</c>'s generated models.
/// </para>
/// </remarks>
public static class SystemOneSchema
{
    private static readonly string[] QuestionTypes = ["noul", "choice", "score"];
    private static readonly string[] QuestionFields = ["type", "instructions", "criteria"];

    /// <summary>Describes how a request fails the schema, or null when it conforms.</summary>
    public static IReadOnlyList<string> Validate(string body)
    {
        var failures = new List<string>();

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            return [$"body: {exception.Message}"];
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return ["body: Input should be a valid dictionary"];
            }

            if (!root.TryGetProperty("state", out var state))
            {
                failures.Add("state: Field required");
            }
            else if (state.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
            {
                failures.Add("state: Input should be a valid string, dictionary or list");
            }

            if (!root.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String)
            {
                failures.Add("model: Field required");
            }

            if (!root.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Object)
            {
                failures.Add("questions: Field required");
                return failures;
            }

            if (!questions.EnumerateObject().Any())
            {
                failures.Add("questions: Dictionary should have at least 1 item after validation, not 0");
            }

            foreach (var question in questions.EnumerateObject())
            {
                ValidateQuestion(question.Name, question.Value, failures);
            }
        }

        return failures;
    }

    private static void ValidateQuestion(string name, JsonElement question, List<string> failures)
    {
        if (question.ValueKind != JsonValueKind.Object)
        {
            failures.Add($"questions.{name}: Input should be a valid dictionary");
            return;
        }

        foreach (var field in question.EnumerateObject())
        {
            if (!QuestionFields.Contains(field.Name, StringComparer.Ordinal))
            {
                failures.Add($"questions.{name}.{field.Name}: Extra inputs are not permitted");
            }
        }

        if (!question.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            failures.Add($"questions.{name}.type: Field required");
            return;
        }

        var kind = type.GetString()!;

        if (!QuestionTypes.Contains(kind, StringComparer.Ordinal))
        {
            failures.Add($"questions.{name}.type: Input tag '{kind}' found using 'type' does not match any of the expected tags");
            return;
        }

        if (question.TryGetProperty("instructions", out var instructions)
            && instructions.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null))
        {
            failures.Add($"questions.{name}.instructions: Input should be a valid string, dictionary or list");
        }

        var hasCriteria = question.TryGetProperty("criteria", out var criteria);

        switch (kind)
        {
            case "choice" when !hasCriteria:
                failures.Add($"questions.{name}.criteria: Field required");
                break;

            case "choice" when criteria.ValueKind != JsonValueKind.Object:
                failures.Add($"questions.{name}.criteria: Input should be a valid dictionary");
                break;

            case "choice" when !criteria.EnumerateObject().Any():
                failures.Add($"questions.{name}.criteria: Dictionary should have at least 1 item after validation, not 0");
                break;

            case "score" when !hasCriteria:
                failures.Add($"questions.{name}.criteria: Field required");
                break;

            case "score" when criteria.ValueKind != JsonValueKind.Array:
                failures.Add($"questions.{name}.criteria: Input should be a valid list");
                break;

            case "score" when criteria.GetArrayLength() == 0:
                failures.Add($"questions.{name}.criteria: List should have at least 1 item after validation, not 0");
                break;

            case "noul" when hasCriteria && criteria.ValueKind == JsonValueKind.Object:
                foreach (var outcome in criteria.EnumerateObject())
                {
                    if (outcome.Name is not ("true" or "false"))
                    {
                        failures.Add($"questions.{name}.criteria.{outcome.Name}: Extra inputs are not permitted");
                    }
                }

                break;

            case "noul" when hasCriteria && criteria.ValueKind != JsonValueKind.Null:
                failures.Add($"questions.{name}.criteria: Input should be a valid dictionary");
                break;

            default:
                break;
        }
    }

    /// <summary>Renders failures as the FastAPI validation body the live API returns.</summary>
    public static string ToValidationBody(IReadOnlyList<string> failures)
    {
        var entries = failures.Select(failure =>
        {
            // Failures are written as "<dotted path>: <message>"; anything else is reported
            // against the body as a whole.
            var separator = failure.IndexOf(": ", StringComparison.Ordinal);

            var path = separator < 0
                ? Enumerable.Empty<string>()
                : failure[..separator].Split('.');

            var message = separator < 0 ? failure : failure[(separator + 2)..];

            var location = string.Join(',', path.Prepend("body").Select(Quote));

            return $$"""{"loc":[{{location}}],"msg":{{Quote(message)}},"type":"value_error"}""";
        });

        return $$"""{"detail":[{{string.Join(',', entries)}}]}""";
    }

    private static string Quote(string value) => $"\"{JsonEncodedText.Encode(value)}\"";
}
