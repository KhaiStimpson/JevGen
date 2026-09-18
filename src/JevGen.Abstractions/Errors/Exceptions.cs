namespace JevGen;

/// <summary>Base type for every error raised by JevGen.</summary>
public class JevGenException : Exception
{
    /// <summary>Creates an exception.</summary>
    public JevGenException() { }

    /// <summary>Creates an exception with a message.</summary>
    public JevGenException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and inner cause.</summary>
    public JevGenException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>An error reported by, or while calling, an evaluation provider.</summary>
public class EvaluationProviderException : JevGenException
{
    /// <summary>Creates a provider exception.</summary>
    public EvaluationProviderException() { }

    /// <summary>Creates a provider exception with a message.</summary>
    public EvaluationProviderException(string message) : base(message) { }

    /// <summary>Creates a provider exception with a message and inner cause.</summary>
    public EvaluationProviderException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>The provider that failed.</summary>
    public string? Provider { get; init; }

    /// <summary>The transport status code, when the failure came from an HTTP call.</summary>
    public int? StatusCode { get; init; }

    /// <summary>The provider-assigned request identifier, when known.</summary>
    public string? RequestId { get; init; }

    /// <summary>
    /// Whether the failure is transient and therefore eligible for retry or fallback.
    /// Authentication and validation failures are never transient.
    /// </summary>
    public virtual bool IsTransient => StatusCode is 408 or 429 or >= 500 and < 600;
}

/// <summary>An evaluation exceeded its configured time budget.</summary>
public sealed class EvaluationTimeoutException : EvaluationProviderException
{
    /// <summary>Creates a timeout exception.</summary>
    public EvaluationTimeoutException() : base("The evaluation timed out.") { }

    /// <summary>Creates a timeout exception with a message.</summary>
    public EvaluationTimeoutException(string message) : base(message) { }

    /// <summary>Creates a timeout exception with a message and inner cause.</summary>
    public EvaluationTimeoutException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>The budget that was exceeded, when known.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <inheritdoc />
    public override bool IsTransient => true;
}

/// <summary>The provider rejected the request because a rate limit was exhausted.</summary>
public sealed class EvaluationRateLimitException : EvaluationProviderException
{
    /// <summary>Creates a rate-limit exception.</summary>
    public EvaluationRateLimitException() : base("The provider rate limit was exceeded.") { }

    /// <summary>Creates a rate-limit exception with a message.</summary>
    public EvaluationRateLimitException(string message) : base(message) { }

    /// <summary>Creates a rate-limit exception with a message and inner cause.</summary>
    public EvaluationRateLimitException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>How long the provider asked the caller to wait, when reported.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <inheritdoc />
    public override bool IsTransient => true;
}

/// <summary>The provider rejected the credentials supplied for the request.</summary>
public sealed class EvaluationAuthenticationException : EvaluationProviderException
{
    /// <summary>Creates an authentication exception.</summary>
    public EvaluationAuthenticationException() : base("The provider rejected the supplied credentials.") { }

    /// <summary>Creates an authentication exception with a message.</summary>
    public EvaluationAuthenticationException(string message) : base(message) { }

    /// <summary>Creates an authentication exception with a message and inner cause.</summary>
    public EvaluationAuthenticationException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Authentication failures are never retried or failed over.</summary>
    public override bool IsTransient => false;
}

/// <summary>State could not be serialized, or a provider payload could not be parsed.</summary>
public sealed class EvaluationSerializationException : JevGenException
{
    /// <summary>Creates a serialization exception.</summary>
    public EvaluationSerializationException() { }

    /// <summary>Creates a serialization exception with a message.</summary>
    public EvaluationSerializationException(string message) : base(message) { }

    /// <summary>Creates a serialization exception with a message and inner cause.</summary>
    public EvaluationSerializationException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>A provider returned a response that does not satisfy the contract.</summary>
public sealed class EvaluationResponseException : EvaluationProviderException
{
    /// <summary>Creates a response exception.</summary>
    public EvaluationResponseException() { }

    /// <summary>Creates a response exception with a message.</summary>
    public EvaluationResponseException(string message) : base(message) { }

    /// <summary>Creates a response exception with a message and inner cause.</summary>
    public EvaluationResponseException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>A malformed response is a deterministic failure, not a transient one.</summary>
    public override bool IsTransient => false;
}

/// <summary>
/// A contract requires semantics the selected provider does not support.
/// </summary>
/// <remarks>
/// JevGen fails rather than silently degrading: a contract that asks for probabilities must
/// not quietly receive a bare answer.
/// </remarks>
public sealed class EvaluationCapabilityException : JevGenException
{
    /// <summary>Creates a capability exception.</summary>
    public EvaluationCapabilityException() { }

    /// <summary>Creates a capability exception with a message.</summary>
    public EvaluationCapabilityException(string message) : base(message) { }

    /// <summary>Creates a capability exception with a message and inner cause.</summary>
    public EvaluationCapabilityException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>The provider that was asked to run the contract.</summary>
    public string? Provider { get; init; }

    /// <summary>The contract member that could not be satisfied.</summary>
    public string? Contract { get; init; }

    /// <summary>The capabilities the contract requires that the provider does not support.</summary>
    public JevCapabilitySet Missing { get; init; }
}

/// <summary>No provider could answer the request, after exhausting the configured fallbacks.</summary>
public sealed class EvaluationFallbackExhaustedException : JevGenException
{
    /// <summary>Creates a fallback-exhausted exception.</summary>
    public EvaluationFallbackExhaustedException() { }

    /// <summary>Creates a fallback-exhausted exception with a message.</summary>
    public EvaluationFallbackExhaustedException(string message) : base(message) { }

    /// <summary>Creates a fallback-exhausted exception with a message and inner cause.</summary>
    public EvaluationFallbackExhaustedException(string message, Exception? innerException)
        : base(message, innerException) { }

    /// <summary>The providers that were tried, in order.</summary>
    public IReadOnlyList<string> AttemptedProviders { get; init; } = [];
}
