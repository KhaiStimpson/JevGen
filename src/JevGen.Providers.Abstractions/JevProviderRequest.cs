using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace JevGen.Providers;

/// <summary>
/// The canonical request handed to a provider. It preserves Jev semantics independently of
/// transport, so a provider only has to translate, never to interpret.
/// </summary>
public sealed record JevProviderRequest
{
    /// <summary>The state the questions are evaluated against.</summary>
    public required object State { get; init; }

    /// <summary>
    /// Serialization metadata for <see cref="State"/>, supplied by generated clients so that
    /// providers can serialize without reflection.
    /// </summary>
    public JsonTypeInfo? StateTypeInfo { get; init; }

    /// <summary>The questions to evaluate.</summary>
    public required ImmutableArray<JevQuestionDefinition> Questions { get; init; }

    /// <summary>The model the provider should use, when the contract or configuration selected one.</summary>
    public string? Model { get; init; }

    /// <summary>The contract interface this request came from.</summary>
    public string? ClientName { get; init; }

    /// <summary>The contract method this request came from.</summary>
    public string? MethodName { get; init; }

    /// <summary>
    /// Extension data scoped to this provider only. Options declared for other providers are
    /// filtered out before the request reaches a provider.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ProviderOptions { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>Free-form request metadata, such as correlation identifiers.</summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>
    /// Serializes <see cref="State"/> to a <see cref="JsonElement"/>, preferring the supplied
    /// <see cref="StateTypeInfo"/> so the call stays trim- and AOT-safe.
    /// </summary>
    /// <param name="fallbackOptions">
    /// Options used only when no <see cref="StateTypeInfo"/> was supplied. These must carry a
    /// type-info resolver that can describe the state type.
    /// </param>
    /// <exception cref="EvaluationSerializationException">The state could not be serialized.</exception>
    [UnconditionalSuppressMessage(
        "AotAnalysis",
        "IL3050:RequiresDynamicCode",
        Justification = "The reflection-free path is used whenever StateTypeInfo is supplied, which generated clients always do. The fallback requires caller-supplied options with a resolver.")]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "See above; the fallback is only reachable for hand-written requests outside trimmed applications.")]
    public JsonElement SerializeState(JsonSerializerOptions? fallbackOptions = null)
    {
        try
        {
            return StateTypeInfo is not null
                ? JsonSerializer.SerializeToElement(State, StateTypeInfo)
                : JsonSerializer.SerializeToElement(State, State.GetType(), fallbackOptions ?? JsonSerializerOptions.Default);
        }
        catch (JsonException exception)
        {
            throw new EvaluationSerializationException(
                $"The state of type '{State.GetType()}' could not be serialized for evaluation.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new EvaluationSerializationException(
                $"The state of type '{State.GetType()}' could not be serialized for evaluation. " +
                "Supply a JsonSerializerContext for the state type so serialization stays reflection-free.",
                exception);
        }
    }
}
