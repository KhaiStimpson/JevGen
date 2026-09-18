using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace JevGen.Generator;

/// <summary>
/// Turns an annotated interface into the immutable contract model the emitters consume,
/// reporting every problem it finds as a compiler diagnostic rather than generating
/// code that will not compile.
/// </summary>
internal sealed partial class ContractParser
{
    private readonly Action<DiagnosticInfo> _report;
    private readonly HashSet<string> _enumsReported = new(StringComparer.Ordinal);

    internal ContractParser(Action<DiagnosticInfo> report) => _report = report;

    internal ClientModel? Parse(INamedTypeSymbol contract, AttributeData clientAttribute, CancellationToken cancellationToken)
    {
        if (contract.TypeKind != TypeKind.Interface)
        {
            Report(JevDiagnostics.ClientMustBeInterface, contract.LocationOf(), contract.ToDisplay());
            return null;
        }

        if (contract.IsGenericType)
        {
            Report(JevDiagnostics.UnsupportedGenericClient, contract.LocationOf(), contract.ToDisplay());
            return null;
        }

        var interfacePolicy = ParsePolicy(contract, contract.ToDisplay());
        var methods = ImmutableArray.CreateBuilder<MethodModel>();

        foreach (var member in contract.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                continue;
            }

            var model = ParseMethod(contract, method, interfacePolicy, cancellationToken);

            if (model is not null)
            {
                methods.Add(model);
            }
        }

        if (methods.Count == 0)
        {
            // Nothing to generate, but the interface is still valid: an empty contract is a
            // work-in-progress, not an error.
            return null;
        }

        return new ClientModel
        {
            Namespace = contract.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : contract.ContainingNamespace.ToDisplayString(),
            InterfaceName = contract.Name,
            FullyQualifiedInterfaceName = contract.ToFullyQualified(),
            DisplayName = clientAttribute.GetNamedString("Name") ?? contract.Name,
            IsPublic = contract.DeclaredAccessibility == Accessibility.Public,
            Methods = new EquatableArray<MethodModel>(methods.ToImmutable()),
            ContractVersion = clientAttribute.GetNamedString("Version"),
            Provider = clientAttribute.GetNamedString("Provider"),
            Model = clientAttribute.GetNamedString("Model"),
            ProviderOptions = ParseProviderOptions(contract),
        };
    }

    private MethodModel? ParseMethod(
        INamedTypeSymbol contract,
        IMethodSymbol method,
        DecisionPolicyModel? interfacePolicy,
        CancellationToken cancellationToken)
    {
        var display = $"{contract.Name}.{method.Name}";

        if (!TryUnwrapAsyncReturn(method.ReturnType, out var isTask, out var resultType))
        {
            Report(JevDiagnostics.UnsupportedReturnType, method.LocationOf(), display, method.ReturnType.ToDisplay());
            return null;
        }

        if (!TryParseParameters(method, display, out var state, out var stateName, out var contextParameters, out var cancellationTokenName))
        {
            return null;
        }

        ValidateStateSerializable(method, state!, display);

        var questionAttributes = method.GetAttributes()
            .Where(attribute =>
                attribute.Matches(KnownNames.JevNoulAttribute)
                || attribute.Matches(KnownNames.JevChoiceAttribute)
                || attribute.Matches(KnownNames.JevScoreAttribute)
                || attribute.Matches(KnownNames.JevEvaluateAttribute))
            .ToList();

        if (questionAttributes.Count == 0)
        {
            Report(JevDiagnostics.QuestionAttributeMissing, method.LocationOf(), display);
            return null;
        }

        if (questionAttributes.Count > 1)
        {
            Report(JevDiagnostics.DuplicateQuestionAttributes, method.LocationOf(), display, questionAttributes.Count);
            return null;
        }

        var attribute = questionAttributes[0];
        var policy = ParsePolicy(method, display) ?? interfacePolicy;

        EquatableArray<QuestionModel> questions;
        AggregateModel? aggregate = null;
        ResultShape shape;

        if (attribute.Matches(KnownNames.JevEvaluateAttribute))
        {
            aggregate = ParseAggregate(resultType!, method.LocationOf(), display, string.Empty, policy, new HashSet<string>(StringComparer.Ordinal), cancellationToken);

            if (aggregate is null)
            {
                return null;
            }

            shape = ResultShape.Aggregate;
            questions = new EquatableArray<QuestionModel>(Flatten(aggregate).ToImmutableArray());

            if (questions.IsEmpty)
            {
                Report(JevDiagnostics.QuestionAttributeMissing, method.LocationOf(), resultType!.ToDisplay());
                return null;
            }
        }
        else
        {
            var question = ParseQuestion(
                attribute,
                method,
                ToDefaultId(method.Name),
                resultType!,
                method.LocationOf(),
                display,
                policy);

            if (question is null)
            {
                return null;
            }

            shape = question.Shape;
            questions = new EquatableArray<QuestionModel>(ImmutableArray.Create(question));
        }

        if (!ValidateUniqueIds(questions, method.LocationOf(), display))
        {
            return null;
        }

        return new MethodModel
        {
            Name = method.Name,
            IsTask = isTask,
            ReturnTypeName = resultType!.ToFullyQualified(),
            Shape = shape,
            StateParameterName = state!.Name,
            StateTypeName = state.Type.ToFullyQualified(),
            StateName = stateName,
            ContextParameters = contextParameters,
            CancellationTokenParameterName = cancellationTokenName,
            SensitiveProperties = CollectSensitiveProperties(state.Type),
            Questions = questions,
            Aggregate = aggregate,
            Model = attribute.GetNamedString("Model"),
            Provider = attribute.GetNamedString("Provider"),
            ProviderOptions = ParseProviderOptions(method),
        };
    }

    private static IEnumerable<QuestionModel> Flatten(AggregateModel aggregate)
    {
        foreach (var property in aggregate.Properties)
        {
            if (property.Question is { } question)
            {
                yield return question;
            }
            else if (property.Nested is { } nested)
            {
                foreach (var inner in Flatten(nested))
                {
                    yield return inner;
                }
            }
        }
    }

    private AggregateModel? ParseAggregate(
        ITypeSymbol resultType,
        Location location,
        string display,
        string prefix,
        DecisionPolicyModel? policy,
        HashSet<string> visiting,
        CancellationToken cancellationToken)
    {
        if (resultType is not INamedTypeSymbol named || named.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            Report(JevDiagnostics.UnsupportedReturnType, location, display, resultType.ToDisplay());
            return null;
        }

        var key = named.ToFullyQualified();

        if (!visiting.Add(key))
        {
            Report(JevDiagnostics.UnsupportedPropertyResultType, location, display, resultType.ToDisplay());
            return null;
        }

        var properties = ImmutableArray.CreateBuilder<AggregatePropertyModel>();
        var failed = false;

        foreach (var member in named.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is not IPropertySymbol
                {
                    IsStatic: false,
                    IsIndexer: false,
                    DeclaredAccessibility: Accessibility.Public,
                } property)
            {
                continue;
            }

            // Records synthesise EqualityContract; it never carries a question.
            if (property.Name == "EqualityContract")
            {
                continue;
            }

            var attributes = property.GetAttributes()
                .Where(attribute =>
                    attribute.Matches(KnownNames.JevNoulAttribute)
                    || attribute.Matches(KnownNames.JevChoiceAttribute)
                    || attribute.Matches(KnownNames.JevScoreAttribute))
                .ToList();

            var propertyDisplay = $"{named.ToDisplay()}.{property.Name}";

            if (attributes.Count > 1)
            {
                Report(JevDiagnostics.DuplicateQuestionAttributes, property.LocationOf(), propertyDisplay, attributes.Count);
                failed = true;
                continue;
            }

            if (attributes.Count == 1)
            {
                var question = ParseQuestion(
                    attributes[0],
                    property,
                    Combine(prefix, ToDefaultId(property.Name)),
                    property.Type,
                    property.LocationOf(),
                    propertyDisplay,
                    ParsePolicy(property, propertyDisplay) ?? policy);

                if (question is null)
                {
                    failed = true;
                    continue;
                }

                properties.Add(new AggregatePropertyModel
                {
                    PropertyName = property.Name,
                    TypeName = property.Type.ToFullyQualified(),
                    Question = question,
                });

                continue;
            }

            // A property with no question attribute is a nested result type when it looks like
            // one, and otherwise simply is not part of the evaluation.
            if (IsNestedResultCandidate(property.Type))
            {
                var nested = ParseAggregate(
                    property.Type,
                    property.LocationOf(),
                    propertyDisplay,
                    Combine(prefix, ToDefaultId(property.Name)),
                    policy,
                    visiting,
                    cancellationToken);

                if (nested is null)
                {
                    failed = true;
                    continue;
                }

                properties.Add(new AggregatePropertyModel
                {
                    PropertyName = property.Name,
                    TypeName = property.Type.ToFullyQualified(),
                    Nested = nested,
                });

                continue;
            }

            if (property.IsRequired)
            {
                Report(JevDiagnostics.UnsupportedPropertyResultType, property.LocationOf(), propertyDisplay, property.Type.ToDisplay());
                failed = true;
            }
        }

        visiting.Remove(key);

        if (failed)
        {
            return null;
        }

        if (!HasUsableConstructor(named))
        {
            Report(JevDiagnostics.UnsupportedPropertyResultType, location, display, named.ToDisplay());
            return null;
        }

        return new AggregateModel
        {
            TypeName = named.ToFullyQualified(),
            Properties = new EquatableArray<AggregatePropertyModel>(properties.ToImmutable()),
        };
    }

    private static bool HasUsableConstructor(INamedTypeSymbol type)
        => type.IsValueType
           || type.InstanceConstructors.Any(constructor =>
               constructor.Parameters.Length == 0
               && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal);

    private static bool IsNestedResultCandidate(ITypeSymbol type)
        => type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named
           && named.SpecialType == SpecialType.None
           && !IsResultType(named)
           && named.GetMembers().OfType<IPropertySymbol>().Any(property =>
               property.GetAttributes().Any(attribute =>
                   attribute.Matches(KnownNames.JevNoulAttribute)
                   || attribute.Matches(KnownNames.JevChoiceAttribute)
                   || attribute.Matches(KnownNames.JevScoreAttribute)));

    private static bool IsResultType(INamedTypeSymbol type)
        => type.ConstructedFrom.ContainingNamespace?.ToDisplayString() == "JevGen"
           && type.ConstructedFrom.MetadataName
               is "NoulResult" or "ScoreResult" or "ChoiceResult`1" or "Decision`1";

    private static string Combine(string prefix, string id)
        => string.IsNullOrEmpty(prefix) ? id : prefix + "." + id;

    private static string ToDefaultId(string memberName) => SymbolExtensions.ToQuestionId(memberName);

    private bool ValidateUniqueIds(EquatableArray<QuestionModel> questions, Location location, string display)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var question in questions)
        {
            if (!seen.Add(question.Id))
            {
                Report(JevDiagnostics.DuplicateQuestionId, location, question.Id, display);
                return false;
            }
        }

        return true;
    }

    private void Report(DiagnosticDescriptor descriptor, Location location, params object?[] arguments)
        => _report(DiagnosticInfo.Create(descriptor, location, arguments));
}
