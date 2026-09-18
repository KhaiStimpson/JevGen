namespace JevGen.Providers;

/// <summary>Semantics a provider is able to honour.</summary>
/// <remarks>
/// Capabilities are declared, not inferred. JevGen validates a contract's requirements against
/// them at startup and fails rather than degrading silently.
/// </remarks>
[Flags]
public enum JevProviderCapabilities
{
    /// <summary>No capabilities.</summary>
    None = 0,

    /// <summary>Probabilistic propositions.</summary>
    Noul = 1 << 0,

    /// <summary>Selection from a fixed option set.</summary>
    Choice = 1 << 1,

    /// <summary>Numeric ratings on a declared scale.</summary>
    Score = 1 << 2,

    /// <summary>Full probability distributions rather than a single answer.</summary>
    Probabilities = 1 << 3,

    /// <summary>Several questions evaluated against one state in a single request.</summary>
    MultiQuestion = 1 << 4,

    /// <summary>Structured (non-textual) state.</summary>
    StructuredState = 1 << 5,

    /// <summary>Incremental streaming of results.</summary>
    Streaming = 1 << 6,

    /// <summary>Per-request model selection.</summary>
    ModelSelection = 1 << 7,

    /// <summary>Native batching of independent requests.</summary>
    NativeBatching = 1 << 8,
}

/// <summary>Conversions between provider capabilities and contract capability requirements.</summary>
public static class JevProviderCapabilitiesExtensions
{
    /// <summary>Views provider capabilities as a contract capability set.</summary>
    public static JevCapabilitySet ToCapabilitySet(this JevProviderCapabilities capabilities)
        => (JevCapabilitySet)(int)capabilities;

    /// <summary>Views a contract capability set as provider capabilities.</summary>
    public static JevProviderCapabilities ToProviderCapabilities(this JevCapabilitySet capabilities)
        => (JevProviderCapabilities)(int)capabilities;

    /// <summary>Returns the capabilities in <paramref name="required"/> that are not supported.</summary>
    public static JevCapabilitySet Missing(this JevProviderCapabilities supported, JevCapabilitySet required)
        => required & ~supported.ToCapabilitySet();
}
