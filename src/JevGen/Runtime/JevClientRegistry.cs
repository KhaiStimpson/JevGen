using System.Collections.Concurrent;

namespace JevGen;

/// <summary>
/// The registry of generated clients.
/// </summary>
/// <remarks>
/// Generated code populates this from a module initializer, so registration happens when the
/// assembly loads, with no assembly scanning and no reflection. It is what makes the generic
/// <c>AddJevClient&lt;T&gt;()</c> API work without a runtime proxy.
/// </remarks>
public static class JevClientRegistry
{
    private static readonly ConcurrentDictionary<Type, JevClientDescriptor> Descriptors = new();

    /// <summary>Every registered contract, in no particular order.</summary>
    public static IReadOnlyCollection<JevClientDescriptor> All => (IReadOnlyCollection<JevClientDescriptor>)Descriptors.Values;

    /// <summary>
    /// Registers a generated client. Called by generated module initializers; calling it by
    /// hand is supported but rarely necessary.
    /// </summary>
    public static void Register(JevClientDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptors[descriptor.ContractType] = descriptor;
    }

    /// <summary>Looks up the descriptor for a contract type.</summary>
    public static bool TryGet(Type contractType, out JevClientDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        return Descriptors.TryGetValue(contractType, out descriptor!);
    }

    /// <summary>Looks up the descriptor for a contract type.</summary>
    /// <exception cref="JevGenException">The contract has no generated client.</exception>
    public static JevClientDescriptor Get(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        if (!Descriptors.TryGetValue(contractType, out var descriptor))
        {
            throw new JevGenException(
                $"No generated JevGen client is registered for '{contractType}'. Apply [JevClient] to " +
                "the interface and make sure the assembly that declares it references the JevGen " +
                "package so the source generator runs over it.");
        }

        return descriptor;
    }

    /// <summary>Looks up the descriptor for a contract type.</summary>
    /// <exception cref="JevGenException">The contract has no generated client.</exception>
    public static JevClientDescriptor Get<TContract>()
        where TContract : class
        => Get(typeof(TContract));

    /// <summary>Creates a client instance over the supplied runtime.</summary>
    /// <exception cref="JevGenException">The contract has no generated client.</exception>
    public static TContract Create<TContract>(IEvaluationRuntime runtime)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return (TContract)Get(typeof(TContract)).Factory(runtime);
    }
}
