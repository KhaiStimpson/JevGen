using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace JevGen.Generator;

internal sealed partial class ContractParser
{
    private bool TryParseParameters(
        IMethodSymbol method,
        string display,
        out IParameterSymbol? state,
        out string? stateName,
        out EquatableArray<ContextParameterModel> contextParameters,
        out string? cancellationTokenParameterName)
    {
        state = null;
        stateName = null;
        contextParameters = EquatableArray<ContextParameterModel>.Empty;
        cancellationTokenParameterName = null;

        var explicitStates = ImmutableArray.CreateBuilder<IParameterSymbol>();
        var implicitStates = ImmutableArray.CreateBuilder<IParameterSymbol>();
        var contexts = ImmutableArray.CreateBuilder<ContextParameterModel>();
        var cancellationTokens = ImmutableArray.CreateBuilder<IParameterSymbol>();

        foreach (var parameter in method.Parameters)
        {
            if (IsCancellationToken(parameter.Type))
            {
                cancellationTokens.Add(parameter);
                continue;
            }

            var contextAttribute = parameter.FindAttribute(KnownNames.ContextAttribute);

            if (contextAttribute is not null)
            {
                var name = contextAttribute.GetConstructorString(0) ?? parameter.Name;
                contexts.Add(new ContextParameterModel(name, parameter.Name, parameter.Type.ToFullyQualified()));
                continue;
            }

            var stateAttribute = parameter.FindAttribute(KnownNames.StateAttribute);

            if (stateAttribute is not null)
            {
                explicitStates.Add(parameter);
                stateName ??= stateAttribute.GetNamedString("Name");
                continue;
            }

            implicitStates.Add(parameter);
        }

        if (cancellationTokens.Count > 1)
        {
            Report(JevDiagnostics.DuplicateCancellationToken, method.LocationOf(), display);
            return false;
        }

        if (cancellationTokens.Count == 1)
        {
            var token = cancellationTokens[0];
            cancellationTokenParameterName = token.Name;

            if (token.Ordinal != method.Parameters.Length - 1)
            {
                Report(JevDiagnostics.CancellationTokenPosition, token.LocationOf(), display);
            }
        }
        else
        {
            Report(JevDiagnostics.MissingCancellationToken, method.LocationOf(), display);
        }

        if (explicitStates.Count > 1)
        {
            Report(JevDiagnostics.MultipleStateParameters, method.LocationOf(), display, explicitStates.Count);
            return false;
        }

        if (explicitStates.Count == 1)
        {
            state = explicitStates[0];

            // Anything left unmarked alongside an explicit [State] is ambiguous: the developer
            // meant it as context but did not say so.
            if (implicitStates.Count > 0)
            {
                Report(
                    JevDiagnostics.MultipleStateParameters,
                    method.LocationOf(),
                    display,
                    explicitStates.Count + implicitStates.Count);
                return false;
            }
        }
        else
        {
            if (implicitStates.Count == 0)
            {
                Report(JevDiagnostics.MissingStateParameter, method.LocationOf(), display);
                return false;
            }

            if (implicitStates.Count > 1)
            {
                Report(JevDiagnostics.MultipleStateParameters, method.LocationOf(), display, implicitStates.Count);
                return false;
            }

            state = implicitStates[0];
        }

        contextParameters = new EquatableArray<ContextParameterModel>(contexts.ToImmutable());
        return true;
    }

    private void ValidateStateSerializable(IMethodSymbol method, IParameterSymbol state, string display)
    {
        var type = state.Type;

        string? reason = type switch
        {
            { TypeKind: TypeKind.Interface } => "an interface has no concrete shape to serialize",
            { TypeKind: TypeKind.TypeParameter } => "an open generic type parameter has no concrete shape to serialize",
            { TypeKind: TypeKind.Delegate } => "a delegate cannot be serialized",
            { TypeKind: TypeKind.Pointer } => "a pointer cannot be serialized",
            { IsAbstract: true, TypeKind: TypeKind.Class } => "an abstract type has no concrete shape to serialize",
            { SpecialType: SpecialType.System_Object } => "System.Object carries no properties to describe the state",
            _ => null,
        };

        if (reason is not null)
        {
            Report(JevDiagnostics.StateNotSerializable, state.LocationOf(), type.ToDisplay(), display, reason);
        }

        _ = method;
    }

    private static bool IsCancellationToken(ITypeSymbol type)
        => type.ToQualifiedName() == KnownNames.CancellationToken;
}
