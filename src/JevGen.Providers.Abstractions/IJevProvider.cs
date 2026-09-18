namespace JevGen.Providers;

/// <summary>
/// The extension point for hosting Jev contracts on any backend.
/// </summary>
/// <remarks>
/// <para>
/// This interface, together with <see cref="JevProviderRequest"/> and
/// <see cref="JevProviderResponse"/>, is a public, documented and versioned extension
/// contract. Third-party providers ship as independent NuGet packages: implementing one
/// requires no generator changes, no new attributes, no runtime reflection and no change to
/// application contracts.
/// </para>
/// <para>
/// A provider translates the canonical request into its own protocol and maps the response
/// back. It should not interpret Jev semantics beyond that translation.
/// </para>
/// </remarks>
public interface IJevProvider
{
    /// <summary>
    /// The registered name of this provider, used for selection and diagnostics.
    /// Names are compared ordinally and case-insensitively.
    /// </summary>
    string Name { get; }

    /// <summary>The semantics this provider can honour.</summary>
    JevProviderCapabilities Capabilities { get; }

    /// <summary>Evaluates the questions in <paramref name="request"/> against its state.</summary>
    ValueTask<JevProviderResponse> EvaluateAsync(
        JevProviderRequest request,
        CancellationToken cancellationToken = default);
}
