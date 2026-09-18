namespace JevGen.Providers;

/// <summary>
/// Provider-reported detail about how a request was served.
/// </summary>
/// <remarks>
/// <see cref="Properties"/> carries provider-specific data — OpenRouter routing decisions,
/// internal gateway identifiers — without changing the core result contracts.
/// </remarks>
public sealed record JevProviderMetadata
{
    /// <summary>The provider that served the request.</summary>
    public required string Provider { get; init; }

    /// <summary>The model that answered, when known.</summary>
    public string? Model { get; init; }

    /// <summary>The provider-assigned request identifier, when known.</summary>
    public string? RequestId { get; init; }

    /// <summary>Provider-specific detail.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; init; }
        = new Dictionary<string, object?>();
}
