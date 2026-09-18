using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace JevGen.Generator;

internal sealed partial class ContractParser
{
    private QuestionModel? ParseQuestion(
        AttributeData attribute,
        ISymbol member,
        string defaultId,
        ITypeSymbol resultType,
        Location location,
        string display,
        DecisionPolicyModel? policy)
    {
        var prompt = attribute.GetConstructorString(0);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            Report(JevDiagnostics.QuestionAttributeMissing, location, display);
            return null;
        }

        var id = attribute.GetNamedString("Id") ?? defaultId;
        var model = attribute.GetNamedString("Model");
        var provider = attribute.GetNamedString("Provider");
        var allowPrimitive = attribute.GetNamedBool("AllowPrimitiveResult") ?? false;

        if (attribute.Matches(KnownNames.JevNoulAttribute))
        {
            var shape = MapNoulShape(resultType, allowPrimitive);

            if (shape is null)
            {
                Report(JevDiagnostics.UnsupportedReturnType, location, display, resultType.ToDisplay());
                return null;
            }

            return new QuestionModel
            {
                Id = id,
                Kind = QuestionKind.Noul,
                Prompt = prompt!,
                Shape = shape.Value,
                Model = model,
                Provider = provider,
            };
        }

        if (attribute.Matches(KnownNames.JevChoiceAttribute))
        {
            return ParseChoice(attribute, member, id, prompt!, resultType, location, display, policy, allowPrimitive, model, provider);
        }

        if (attribute.Matches(KnownNames.JevScoreAttribute))
        {
            return ParseScore(attribute, id, prompt!, resultType, location, display, allowPrimitive, model, provider);
        }

        Report(JevDiagnostics.QuestionAttributeMissing, location, display);
        return null;
    }

    private static ResultShape? MapNoulShape(ITypeSymbol resultType, bool allowPrimitive)
    {
        if (IsNamed(resultType, KnownNames.NoulResult))
        {
            return ResultShape.Noul;
        }

        if (allowPrimitive && resultType.SpecialType == SpecialType.System_Boolean)
        {
            return ResultShape.PrimitiveBoolean;
        }

        return null;
    }

    private QuestionModel? ParseChoice(
        AttributeData attribute,
        ISymbol member,
        string id,
        string prompt,
        ITypeSymbol resultType,
        Location location,
        string display,
        DecisionPolicyModel? policy,
        bool allowPrimitive,
        string? model,
        string? provider)
    {
        var requireProbabilities = attribute.GetNamedBool("RequireProbabilities") ?? true;

        ITypeSymbol? enumType;
        ResultShape shape;

        if (TryGetGenericArgument(resultType, "ChoiceResult`1", out enumType))
        {
            shape = ResultShape.Choice;
        }
        else if (TryGetGenericArgument(resultType, "Decision`1", out enumType))
        {
            shape = ResultShape.Decision;
        }
        else if (allowPrimitive && resultType.TypeKind == TypeKind.Enum)
        {
            enumType = resultType;
            shape = ResultShape.PrimitiveEnum;
        }
        else
        {
            Report(JevDiagnostics.UnsupportedReturnType, location, display, resultType.ToDisplay());
            return null;
        }

        if (enumType is null || enumType.TypeKind != TypeKind.Enum)
        {
            Report(JevDiagnostics.UnsupportedChoiceType, location, enumType?.ToDisplay() ?? resultType.ToDisplay());
            return null;
        }

        // A shape that cannot carry a distribution must not claim to require one.
        if (requireProbabilities && shape is ResultShape.PrimitiveEnum)
        {
            Report(JevDiagnostics.ProviderCapabilityUnsupported, location, display, "Probabilities", resultType.ToDisplay());
            requireProbabilities = false;
        }

        var options = ParseChoiceOptions((INamedTypeSymbol)enumType, member, location);

        if (options.IsEmpty)
        {
            Report(JevDiagnostics.UnsupportedChoiceType, location, enumType.ToDisplay());
            return null;
        }

        if (shape == ResultShape.Decision && policy is { } declared && !ValidatePolicy(declared, location, display))
        {
            return null;
        }

        return new QuestionModel
        {
            Id = id,
            Kind = QuestionKind.Choice,
            Prompt = prompt,
            Shape = shape,
            ChoiceTypeName = enumType.ToFullyQualified(),
            Options = options,
            RequiresProbabilities = requireProbabilities,
            Model = model,
            Provider = provider,
            Policy = shape == ResultShape.Decision ? policy ?? DefaultPolicy : null,
        };
    }

    private static readonly DecisionPolicyModel DefaultPolicy = new(0.9d, 0.65d);

    private EquatableArray<ChoiceOptionModel> ParseChoiceOptions(
        INamedTypeSymbol enumType,
        ISymbol member,
        Location fallbackLocation)
    {
        var options = ImmutableArray.CreateBuilder<ChoiceOptionModel>();
        var reportCriteria = _enumsReported.Add(enumType.ToFullyQualified());

        foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.IsStatic || field.ConstantValue is null)
            {
                continue;
            }

            var option = field.FindAttribute(KnownNames.JevOptionAttribute);

            if (option is not null && (option.GetNamedBool("Exclude") ?? false))
            {
                continue;
            }

            var optionId = option?.GetConstructorString(0) ?? SymbolExtensions.ToQuestionId(field.Name);
            var criteria = option?.GetConstructorString(1);

            if (criteria is null && reportCriteria)
            {
                var location = field.LocationOf();
                Report(
                    JevDiagnostics.MissingEnumOptionCriteria,
                    location == Location.None ? fallbackLocation : location,
                    enumType.ToDisplay(),
                    field.Name);
            }

            options.Add(new ChoiceOptionModel(optionId, criteria, field.Name));
        }

        _ = member;
        return new EquatableArray<ChoiceOptionModel>(options.ToImmutable());
    }

    private QuestionModel? ParseScore(
        AttributeData attribute,
        string id,
        string prompt,
        ITypeSymbol resultType,
        Location location,
        string display,
        bool allowPrimitive,
        string? model,
        string? provider)
    {
        ResultShape shape;

        if (IsNamed(resultType, KnownNames.ScoreResult))
        {
            shape = ResultShape.Score;
        }
        else if (allowPrimitive && resultType.SpecialType is SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Int32)
        {
            shape = ResultShape.PrimitiveScore;
        }
        else
        {
            Report(JevDiagnostics.UnsupportedReturnType, location, display, resultType.ToDisplay());
            return null;
        }

        var criteria = attribute.ConstructorArguments.Length > 1
            ? attribute.ConstructorArguments[1].Values
                .Select(value => value.Value as string)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;

        var min = attribute.GetNamedDouble("Min");
        var max = attribute.GetNamedDouble("Max");

        if (min.HasValue != max.HasValue)
        {
            Report(
                JevDiagnostics.InvalidScoreDefinition,
                location,
                display,
                "declare both Min and Max, or neither");
            return null;
        }

        if (min.HasValue && max.HasValue && min.Value >= max.Value)
        {
            Report(
                JevDiagnostics.InvalidScoreDefinition,
                location,
                display,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Min ({0}) must be less than Max ({1})",
                    min.Value,
                    max.Value));
            return null;
        }

        if (!min.HasValue && criteria.IsEmpty)
        {
            Report(
                JevDiagnostics.InvalidScoreDefinition,
                location,
                display,
                "no scale is declared. Supply Min and Max bounds, or ordered rubric criteria");
            return null;
        }

        if (!min.HasValue)
        {
            // Rubric labels imply a one-based scale with one point per label.
            min = 1d;
            max = criteria.Length;
        }

        return new QuestionModel
        {
            Id = id,
            Kind = QuestionKind.Score,
            Prompt = prompt,
            Shape = shape,
            Minimum = min,
            Maximum = max,
            Criteria = new EquatableArray<string>(criteria),
            Model = model,
            Provider = provider,
        };
    }

    private DecisionPolicyModel? ParsePolicy(ISymbol symbol, string display)
    {
        var attribute = symbol.FindAttribute(KnownNames.DecisionPolicyAttribute);

        if (attribute is null)
        {
            return null;
        }

        var policy = new DecisionPolicyModel(
            attribute.GetNamedDouble("AcceptAbove") ?? DefaultPolicy.AcceptAbove,
            attribute.GetNamedDouble("ReviewAbove") ?? DefaultPolicy.ReviewAbove);

        return ValidatePolicy(policy, symbol.LocationOf(), display) ? policy : null;
    }

    private bool ValidatePolicy(DecisionPolicyModel policy, Location location, string display)
    {
        if (policy.AcceptAbove is < 0d or > 1d || policy.ReviewAbove is < 0d or > 1d)
        {
            Report(
                JevDiagnostics.InvalidConfidenceThreshold,
                location,
                display,
                "confidence thresholds must be between 0 and 1");
            return false;
        }

        if (policy.AcceptAbove <= policy.ReviewAbove)
        {
            Report(
                JevDiagnostics.InvalidConfidenceThreshold,
                location,
                display,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "AcceptAbove ({0}) must be greater than ReviewAbove ({1})",
                    policy.AcceptAbove,
                    policy.ReviewAbove));
            return false;
        }

        return true;
    }

    private static bool IsNamed(ITypeSymbol type, string metadataName) => type.ToQualifiedName() == metadataName;

    private static bool TryGetGenericArgument(ITypeSymbol type, string metadataName, out ITypeSymbol? argument)
    {
        if (type is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.MetadataName == metadataName
            && named.ContainingNamespace.ToDisplayString() == "JevGen"
            && named.TypeArguments.Length == 1)
        {
            argument = named.TypeArguments[0];
            return true;
        }

        argument = null;
        return false;
    }

    private static bool TryUnwrapAsyncReturn(ITypeSymbol returnType, out bool isTask, out ITypeSymbol? resultType)
    {
        if (returnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
            && named.ConstructedFrom.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
        {
            switch (named.ConstructedFrom.MetadataName)
            {
                case "Task`1":
                    isTask = true;
                    resultType = named.TypeArguments[0];
                    return true;

                case "ValueTask`1":
                    isTask = false;
                    resultType = named.TypeArguments[0];
                    return true;
            }
        }

        isTask = false;
        resultType = null;
        return false;
    }

    private EquatableArray<ProviderOptionModel> ParseProviderOptions(ISymbol symbol)
    {
        var options = ImmutableArray.CreateBuilder<ProviderOptionModel>();

        foreach (var attribute in symbol.GetAttributes())
        {
            if (!attribute.Matches(KnownNames.JevProviderOptionAttribute)
                || attribute.ConstructorArguments.Length < 3)
            {
                continue;
            }

            var provider = attribute.GetConstructorString(0);
            var key = attribute.GetConstructorString(1);
            var value = attribute.GetConstructorString(2);

            if (provider is null || key is null || value is null)
            {
                continue;
            }

            options.Add(new ProviderOptionModel(provider, key, value));
        }

        return new EquatableArray<ProviderOptionModel>(options.ToImmutable());
    }
}
