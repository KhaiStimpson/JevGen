using System;

namespace JevGen.Generator;

internal enum QuestionKind
{
    Noul = 0,
    Choice = 1,
    Score = 2,
}

/// <summary>How a mapped value is surfaced to the caller.</summary>
internal enum ResultShape
{
    /// <summary>NoulResult.</summary>
    Noul,

    /// <summary>ChoiceResult&lt;TEnum&gt;.</summary>
    Choice,

    /// <summary>ScoreResult.</summary>
    Score,

    /// <summary>Decision&lt;TEnum&gt; produced by applying a declared policy to a choice.</summary>
    Decision,

    /// <summary>A bare bool, opted into explicitly.</summary>
    PrimitiveBoolean,

    /// <summary>A bare enum member, opted into explicitly.</summary>
    PrimitiveEnum,

    /// <summary>A bare double, opted into explicitly.</summary>
    PrimitiveScore,

    /// <summary>An aggregate result type whose properties carry the questions.</summary>
    Aggregate,
}

internal readonly record struct ChoiceOptionModel(string Id, string? Criteria, string MemberName);

internal readonly record struct DecisionPolicyModel(double AcceptAbove, double ReviewAbove);

/// <summary>One question and the member it maps onto.</summary>
internal sealed record QuestionModel : IEquatable<QuestionModel>
{
    public required string Id { get; init; }

    public required QuestionKind Kind { get; init; }

    public required string Prompt { get; init; }

    public required ResultShape Shape { get; init; }

    /// <summary>The fully qualified enum type for a choice, if any.</summary>
    public string? ChoiceTypeName { get; init; }

    public EquatableArray<ChoiceOptionModel> Options { get; init; } = EquatableArray<ChoiceOptionModel>.Empty;

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public EquatableArray<string> Criteria { get; init; } = EquatableArray<string>.Empty;

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public bool RequiresProbabilities { get; init; }

    public DecisionPolicyModel? Policy { get; init; }
}

/// <summary>A property on an aggregate result type, and what fills it.</summary>
internal sealed record AggregatePropertyModel : IEquatable<AggregatePropertyModel>
{
    public required string PropertyName { get; init; }

    public required string TypeName { get; init; }

    /// <summary>The question that fills this property, for a leaf property.</summary>
    public QuestionModel? Question { get; init; }

    /// <summary>The nested result type that fills this property, for a composite property.</summary>
    public AggregateModel? Nested { get; init; }
}

/// <summary>An aggregate result type: several questions answered against one state.</summary>
internal sealed record AggregateModel : IEquatable<AggregateModel>
{
    public required string TypeName { get; init; }

    public required EquatableArray<AggregatePropertyModel> Properties { get; init; }
}

/// <summary>A parameter that contributes a named value to composite state.</summary>
internal readonly record struct ContextParameterModel(string Name, string ParameterName, string TypeName);

/// <summary>A provider-scoped option declared by attribute.</summary>
internal readonly record struct ProviderOptionModel(string Provider, string Key, string Value);

internal sealed record MethodModel : IEquatable<MethodModel>
{
    public required string Name { get; init; }

    /// <summary>True for Task&lt;T&gt;, false for ValueTask&lt;T&gt;.</summary>
    public required bool IsTask { get; init; }

    public required string ReturnTypeName { get; init; }

    public required ResultShape Shape { get; init; }

    public required string StateParameterName { get; init; }

    public required string StateTypeName { get; init; }

    /// <summary>The property name the state appears under, when the state is composed.</summary>
    public string? StateName { get; init; }

    public EquatableArray<ContextParameterModel> ContextParameters { get; init; }
        = EquatableArray<ContextParameterModel>.Empty;

    public string? CancellationTokenParameterName { get; init; }

    /// <summary>Names of state properties marked [JevSensitive].</summary>
    public EquatableArray<string> SensitiveProperties { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The questions this method evaluates, flattened in emission order.</summary>
    public required EquatableArray<QuestionModel> Questions { get; init; }

    /// <summary>The aggregate result structure, when the method returns one.</summary>
    public AggregateModel? Aggregate { get; init; }

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public EquatableArray<ProviderOptionModel> ProviderOptions { get; init; }
        = EquatableArray<ProviderOptionModel>.Empty;

    public bool HasComposedState => StateName is not null || !ContextParameters.IsEmpty;
}

internal sealed record ClientModel : IEquatable<ClientModel>
{
    public required string Namespace { get; init; }

    public required string InterfaceName { get; init; }

    public required string FullyQualifiedInterfaceName { get; init; }

    public required string DisplayName { get; init; }

    public required bool IsPublic { get; init; }

    public required EquatableArray<MethodModel> Methods { get; init; }

    public string? ContractVersion { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public EquatableArray<ProviderOptionModel> ProviderOptions { get; init; }
        = EquatableArray<ProviderOptionModel>.Empty;

    /// <summary>The namespace the generated types are emitted into.</summary>
    public string GeneratedNamespace =>
        string.IsNullOrEmpty(Namespace) ? "JevGen.Generated" : "JevGen.Generated." + Namespace;

    public string ClientTypeName => InterfaceName + "_JevGenClient";

    public string SchemaTypeName => InterfaceName + "_JevGenSchema";

    public string RegistrationTypeName => InterfaceName + "_JevGenRegistration";

    public string HintName =>
        (string.IsNullOrEmpty(Namespace) ? InterfaceName : Namespace + "." + InterfaceName) + ".JevGen.g.cs";
}
