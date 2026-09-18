namespace JevGen;

/// <summary>
/// Semantic capabilities a contract may require and a provider may support.
/// </summary>
/// <remarks>
/// This provider-neutral mirror of the provider SPI's capability flags lets
/// <c>JevGen.Abstractions</c> describe contract requirements without depending on the
/// provider packages. The values are deliberately identical.
/// </remarks>
[Flags]
public enum JevCapabilitySet
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
