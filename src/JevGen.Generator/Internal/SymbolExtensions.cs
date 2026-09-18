using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace JevGen.Generator;

internal static class SymbolExtensions
{
    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static string ToFullyQualified(this ITypeSymbol symbol)
        => symbol.ToDisplayString(FullyQualified);

    internal static string ToDisplay(this ITypeSymbol symbol)
        => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    /// <summary>
    /// The fully qualified name without the <c>global::</c> prefix.
    /// </summary>
    /// <remarks>
    /// Special types render as language keywords ("string", not "global::System.String"), so
    /// the prefix cannot simply be sliced off a fixed offset.
    /// </remarks>
    internal static string ToQualifiedName(this ISymbol symbol)
    {
        var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return name.StartsWith("global::", StringComparison.Ordinal)
            ? name.Substring("global::".Length)
            : name;
    }

    internal static AttributeData? FindAttribute(this ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (Matches(attribute, metadataName))
            {
                return attribute;
            }
        }

        return null;
    }

    internal static bool Matches(this AttributeData attribute, string metadataName)
        => attribute.AttributeClass is { } attributeClass && attributeClass.ToQualifiedName() == metadataName;

    internal static string? GetNamedString(this AttributeData attribute, string name)
        => attribute.NamedArguments
            .FirstOrDefault(pair => pair.Key == name)
            .Value is { IsNull: false, Value: string value } && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    internal static bool? GetNamedBool(this AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (pair.Key == name && pair.Value.Value is bool value)
            {
                return value;
            }
        }

        return null;
    }

    internal static double? GetNamedDouble(this AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (pair.Key == name && pair.Value.Value is { } raw)
            {
                var value = Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture);
                return double.IsNaN(value) ? null : value;
            }
        }

        return null;
    }

    internal static string? GetConstructorString(this AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index
           && attribute.ConstructorArguments[index].Value is string value
            ? value
            : null;

    internal static Location LocationOf(this ISymbol symbol)
        => symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;

    /// <summary>Turns a member name into a stable wire identifier: <c>RouteAsync</c> to <c>route</c>.</summary>
    internal static string ToQuestionId(string memberName)
    {
        var name = memberName;

        if (name.Length > 5 && name.EndsWith("Async", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - 5);
        }

        if (name.Length == 0)
        {
            return memberName;
        }

        if (name.Length == 1)
        {
            return name.ToLowerInvariant();
        }

        // Preserve acronyms: SLABreach stays SLABreach rather than becoming sLABreach.
        if (char.IsUpper(name[0]) && char.IsUpper(name[1]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
