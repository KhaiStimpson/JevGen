namespace JevGen;

/// <summary>
/// Marks the parameter that carries the state a question is evaluated against.
/// </summary>
/// <remarks>
/// The attribute is optional when a method has exactly one non-cancellation parameter: that
/// parameter is treated as the state. Apply it explicitly once a method also takes context
/// parameters.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class StateAttribute : Attribute
{
    /// <summary>
    /// The property name the state appears under in the serialized request.
    /// When omitted the state object's own properties form the root of the state.
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// Marks a parameter that contributes an additional named value to the evaluation state.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ContextAttribute : Attribute
{
    /// <summary>Adds a named context value to the state.</summary>
    /// <param name="name">The property name the value appears under.</param>
    public ContextAttribute(string name) => Name = name;

    /// <summary>The property name the value appears under in the serialized state.</summary>
    public string Name { get; }
}

/// <summary>
/// Describes an enum member that participates in a choice question.
/// </summary>
/// <remarks>
/// The attribute is optional. Without it the member name is camel-cased into a wire identifier
/// and no criteria are sent, which usually gives the model less to work with.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class JevOptionAttribute : Attribute
{
    /// <summary>Describes a choice option.</summary>
    /// <param name="id">The wire identifier for this member.</param>
    /// <param name="criteria">Guidance describing when this option applies.</param>
    public JevOptionAttribute(string id, string? criteria = null)
    {
        Id = id;
        Criteria = criteria;
    }

    /// <summary>The wire identifier for this member.</summary>
    public string Id { get; }

    /// <summary>Guidance describing when this option applies.</summary>
    public string? Criteria { get; }

    /// <summary>
    /// Excludes the member from the option set, for sentinel members that should never be
    /// selected by a model.
    /// </summary>
    public bool Exclude { get; set; }
}

/// <summary>
/// Marks a state property that must never appear in logs, telemetry or diagnostics.
/// </summary>
/// <remarks>
/// The property is still sent to the provider — it is part of the state the model reasons
/// about — but generated logging and debug helpers redact it.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = false)]
public sealed class JevSensitiveAttribute : Attribute;

/// <summary>
/// Supplies provider-specific extension data that the canonical contract does not model.
/// </summary>
/// <remarks>
/// The data is only ever passed to the named provider. It never reaches another one, so a
/// contract annotated for one host stays correct on every other.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Interface | AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = false)]
public sealed class JevProviderOptionAttribute : Attribute
{
    /// <summary>Supplies one provider-specific option.</summary>
    /// <param name="provider">The registered provider name the option applies to.</param>
    /// <param name="key">The option name.</param>
    /// <param name="value">The option value.</param>
    public JevProviderOptionAttribute(string provider, string key, string value)
    {
        Provider = provider;
        Key = key;
        Value = value;
    }

    /// <summary>The provider the option applies to.</summary>
    public string Provider { get; }

    /// <summary>The option name.</summary>
    public string Key { get; }

    /// <summary>The option value.</summary>
    public string Value { get; }
}

/// <summary>
/// Declares confidence thresholds that classify a method's result into a
/// <see cref="DecisionAction"/>.
/// </summary>
/// <remarks>
/// The policy classifies the result only. It never executes business side effects: acting on
/// an <see cref="DecisionAction.Accept"/> remains the application's decision. Apply it to a
/// method returning <see cref="Decision{T}"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class DecisionPolicyAttribute : Attribute
{
    /// <summary>Confidence at or above which the decision is classified as accepted.</summary>
    public double AcceptAbove { get; set; } = 0.9d;

    /// <summary>
    /// Confidence at or above which the decision is classified as needing review.
    /// Below this the decision is rejected.
    /// </summary>
    public double ReviewAbove { get; set; } = 0.65d;
}

/// <summary>
/// Points JevGen at a <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> that
/// describes the assembly's state and result types.
/// </summary>
/// <remarks>
/// <para>
/// Source generators cannot see each other's output, so JevGen cannot emit
/// <c>[JsonSerializable]</c> declarations and have <c>System.Text.Json</c> pick them up.
/// Declaring the context in your own source and naming it here keeps serialization completely
/// reflection-free, which is what Native AOT and trimming require.
/// </para>
/// <para>
/// Without it JevGen falls back to reflection-based serialization, which works on a normal
/// runtime but is neither trim- nor AOT-safe.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [JsonSerializable(typeof(Ticket))]
/// internal partial class AppJsonContext : JsonSerializerContext;
///
/// [assembly: JevJsonContext(typeof(AppJsonContext))]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class JevJsonContextAttribute : Attribute
{
    /// <summary>Names a serializer context for JevGen to use.</summary>
    /// <param name="contextType">A type deriving from <c>JsonSerializerContext</c>.</param>
    public JevJsonContextAttribute(Type contextType) => ContextType = contextType;

    /// <summary>The serializer context type.</summary>
    public Type ContextType { get; }
}
