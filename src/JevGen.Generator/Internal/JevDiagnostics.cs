using Microsoft.CodeAnalysis;

namespace JevGen.Generator;

/// <summary>
/// Every diagnostic JevGen can report. Descriptors live here so the generator and the analyzer
/// package report identical identifiers, titles and help links.
/// </summary>
internal static class JevDiagnostics
{
    private const string Category = "JevGen";

    private static string Help(string id)
        => $"https://github.com/KhaiStimpson/JevGen/blob/main/docs/diagnostics.md#{id.ToLowerInvariant()}";

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string messageFormat,
        DiagnosticSeverity severity,
        string? description = null)
        => new(
            id,
            title,
            messageFormat,
            Category,
            severity,
            isEnabledByDefault: true,
            description: description,
            helpLinkUri: Help(id));

    internal static readonly DiagnosticDescriptor ClientMustBeInterface = Create(
        "JEV001",
        "[JevClient] target must be an interface",
        "'{0}' cannot be a JevGen client because [JevClient] may only be applied to an interface",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor UnsupportedReturnType = Create(
        "JEV002",
        "Unsupported method return type",
        "'{0}' returns '{1}', which JevGen cannot map. Return Task<T> or ValueTask<T> of NoulResult, ChoiceResult<TEnum>, ScoreResult, Decision<TEnum> or a result type whose properties declare questions.",
        DiagnosticSeverity.Error,
        "JevGen maps a method's return type onto a Jev question shape at compile time. Returning a bare primitive discards the confidence and probability data the model produced; opt into it explicitly with AllowPrimitiveResult when that is genuinely what you want.");

    internal static readonly DiagnosticDescriptor MissingStateParameter = Create(
        "JEV003",
        "Missing state parameter",
        "'{0}' declares no state parameter. A Jev question is always evaluated against some state.",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor MultipleStateParameters = Create(
        "JEV004",
        "Multiple state parameters",
        "'{0}' declares {1} parameters marked [State]. Exactly one state parameter is allowed; use [Context(\"name\")] for additional values.",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor UnsupportedChoiceType = Create(
        "JEV005",
        "Unsupported Choice type",
        "'{0}' is not a supported choice type. ChoiceResult<T> and Decision<T> require T to be an enum.",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor InvalidScoreDefinition = Create(
        "JEV006",
        "Invalid score definition",
        "The score question on '{0}' is invalid: {1}",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor MissingEnumOptionCriteria = Create(
        "JEV007",
        "Missing enum option criteria",
        "Choice enum member {0}.{1} has no criteria. Add [JevOption] so the model is told when to select it.",
        DiagnosticSeverity.Warning,
        "Without criteria the model sees only the member name. Describing when an option applies materially improves routing accuracy.");

    internal static readonly DiagnosticDescriptor DuplicateQuestionId = Create(
        "JEV008",
        "Duplicate question ID",
        "Question ID '{0}' is declared more than once on '{1}'. Question IDs must be unique within a single evaluation.",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor UnsupportedPropertyResultType = Create(
        "JEV009",
        "Unsupported property result type",
        "Property '{0}' of type '{1}' cannot carry a question result. Use NoulResult, ChoiceResult<TEnum>, ScoreResult, Decision<TEnum> or a nested result type.",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor DuplicateCancellationToken = Create(
        "JEV010",
        "CancellationToken duplicated",
        "'{0}' declares more than one CancellationToken parameter",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor CancellationTokenPosition = Create(
        "JEV011",
        "CancellationToken position",
        "The CancellationToken parameter of '{0}' should be the last parameter",
        DiagnosticSeverity.Warning);

    internal static readonly DiagnosticDescriptor StateNotSerializable = Create(
        "JEV012",
        "State type cannot be serialized",
        "The state type '{0}' of '{1}' cannot be serialized: {2}",
        DiagnosticSeverity.Warning,
        "Evaluation state is serialized to JSON before it reaches a provider. Abstract, interface and open generic state types have no concrete shape to serialize.");

    internal static readonly DiagnosticDescriptor UnsupportedGenericClient = Create(
        "JEV013",
        "Unsupported generic client",
        "'{0}' is generic. JevGen clients must be non-generic interfaces.",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor QuestionAttributeMissing = Create(
        "JEV014",
        "Question attribute missing",
        "'{0}' declares no question. Apply [JevNoul], [JevChoice], [JevScore] or [JevEvaluate].",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor DuplicateQuestionAttributes = Create(
        "JEV015",
        "Duplicate question attributes",
        "'{0}' declares {1} question attributes. A member declares exactly one question.",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor InvalidConfidenceThreshold = Create(
        "JEV016",
        "Invalid confidence threshold",
        "The decision policy on '{0}' is invalid: {1}",
        DiagnosticSeverity.Error);

    internal static readonly DiagnosticDescriptor ProviderCapabilityUnsupported = Create(
        "JEV017",
        "Provider capability unsupported",
        "'{0}' requires {1}, which cannot be delivered through its return type '{2}'",
        DiagnosticSeverity.Warning,
        "JevGen never degrades semantics silently. A question that asks for a probability distribution must return a shape that can carry one.");

    internal static readonly DiagnosticDescriptor MissingJevClientAttribute = Create(
        "JEV018",
        "Interface declares questions but is not a JevGen client",
        "'{0}' declares Jev questions but is not marked [JevClient], so no client is generated for it",
        DiagnosticSeverity.Warning);

    internal static readonly DiagnosticDescriptor MissingCancellationToken = Create(
        "JEV019",
        "Method takes no CancellationToken",
        "'{0}' takes no CancellationToken, so callers cannot cancel the evaluation",
        DiagnosticSeverity.Info);

    internal static readonly DiagnosticDescriptor MissingSerializerContext = Create(
        "JEV020",
        "No serializer context declared",
        "This assembly declares JevGen clients but no [assembly: JevJsonContext], so state is serialized by reflection, which is not trim- or Native-AOT-safe",
        DiagnosticSeverity.Info);
}
