using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace JevGen;

/// <summary>
/// The serialization entry point shared by generated clients and providers.
/// </summary>
/// <remarks>
/// <para>
/// JevGen never serializes state by reflection when it can avoid it. Generated clients look up
/// <see cref="JsonTypeInfo"/> here; registering a <see cref="JsonSerializerContext"/> — through
/// <see cref="JevJsonContextAttribute"/> or <see cref="AddContext"/> — keeps the whole path
/// reflection-free and therefore trim- and AOT-safe.
/// </para>
/// <para>
/// When no context describes a type, the reflection resolver is used if the runtime still has
/// reflection-based serialization enabled. In a trimmed or AOT application it does not, and
/// lookups return <see langword="null"/> so callers can fail with a clear message instead of
/// crashing deep inside the serializer.
/// </para>
/// </remarks>
public static class JevGenJson
{
    private static readonly Lock Gate = new();
    private static readonly List<IJsonTypeInfoResolver> Resolvers = [];
    private static JsonSerializerOptions _options = CreateOptions();

    /// <summary>
    /// The options used for state serialization. Replacing them replaces the resolver chain,
    /// so do it during start-up, before the first evaluation.
    /// </summary>
    public static JsonSerializerOptions Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (Gate)
            {
                value.MakeReadOnly();
                _options = value;
            }
        }
    }

    /// <summary>
    /// Registers a serializer context so its types can be serialized without reflection.
    /// Contexts registered later take precedence over the reflection fallback but not over
    /// contexts registered earlier.
    /// </summary>
    public static void AddContext(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (Gate)
        {
            if (Resolvers.Contains(context))
            {
                return;
            }

            Resolvers.Add(context);
            _options = CreateOptions();
        }
    }

    /// <summary>
    /// Returns serialization metadata for <paramref name="type"/>, or <see langword="null"/>
    /// when nothing can describe it.
    /// </summary>
    public static JsonTypeInfo? TryGetTypeInfo(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var options = Options;

        try
        {
            return options.TypeInfoResolver?.GetTypeInfo(type, options);
        }
        catch (InvalidOperationException)
        {
            // No resolver could describe the type; the caller reports this as a configuration error.
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns serialization metadata for <paramref name="type"/>, throwing a diagnosable
    /// error when nothing can describe it.
    /// </summary>
    /// <exception cref="EvaluationSerializationException">
    /// No registered context describes the type and reflection-based serialization is disabled.
    /// </exception>
    public static JsonTypeInfo GetTypeInfo(Type type)
        => TryGetTypeInfo(type)
           ?? throw new EvaluationSerializationException(
               $"No JSON serialization metadata is available for '{type}'. Declare a " +
               "JsonSerializerContext for it and point JevGen at the context with " +
               "[assembly: JevJsonContext(typeof(YourContext))], or call JevGenJson.AddContext " +
               "during start-up. This is required in trimmed and Native AOT applications, where " +
               "reflection-based serialization is unavailable.");

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "The reflection resolver is only added when the runtime reports that reflection-based serialization is enabled, which is never the case in a trimmed application.")]
    [UnconditionalSuppressMessage(
        "AotAnalysis",
        "IL3050:RequiresDynamicCode",
        Justification = "See above; in AOT the guard is false and only source-generated contexts are used.")]
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        var resolvers = new List<IJsonTypeInfoResolver>(Resolvers);

        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            resolvers.Add(new DefaultJsonTypeInfoResolver());
        }

        options.TypeInfoResolver = resolvers.Count switch
        {
            0 => JsonTypeInfoResolver.Combine(),
            1 => resolvers[0],
            _ => JsonTypeInfoResolver.Combine([.. resolvers]),
        };

        options.MakeReadOnly();
        return options;
    }
}
