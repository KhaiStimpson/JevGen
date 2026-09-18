namespace JevGen;

/// <summary>
/// Implemented by every generated client. Generated types are internal by design; this marker
/// gives advanced callers a stable way to recognise one.
/// </summary>
public interface IJevClient
{
    /// <summary>The descriptor for the contract this client implements.</summary>
    JevClientDescriptor Descriptor { get; }
}
