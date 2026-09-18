namespace JevGen;

/// <summary>
/// Marks an interface as an AI decision contract. JevGen generates an implementation for it
/// at compile time.
/// </summary>
/// <remarks>
/// The interface must be public or internal, non-generic, and every method must return
/// <see cref="System.Threading.Tasks.Task{TResult}"/> or
/// <see cref="System.Threading.Tasks.ValueTask{TResult}"/> of a supported result shape.
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class JevClientAttribute : Attribute
{
    /// <summary>
    /// The registered provider name every method on this contract should use, unless a method
    /// overrides it. Programmatic configuration takes precedence where it is set explicitly.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>The model every method on this contract should use, unless a method overrides it.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// A contract version, recorded on evaluations for audit logging, dataset compatibility and
    /// rollout tracking.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// The name recorded for this contract in telemetry and audit records.
    /// Defaults to the interface name.
    /// </summary>
    public string? Name { get; set; }
}
