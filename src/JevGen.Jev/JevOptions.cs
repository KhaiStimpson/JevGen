namespace JevGen.Jev;

/// <summary>Configuration shared by every Jev-protocol provider.</summary>
public class JevOptions
{
    /// <summary>The API key used to authenticate. Never logged, never placed in telemetry.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The model to use when a contract does not select one.</summary>
    public string Model { get; set; } = JevModel.Latest;

    /// <summary>The endpoint to call. Providers supply their own default.</summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>The per-request transport timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Extra headers sent with every request.</summary>
    public IDictionary<string, string> DefaultHeaders { get; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Validates the options, throwing when the provider cannot work as configured.</summary>
    /// <exception cref="EvaluationAuthenticationException">No API key is configured.</exception>
    public virtual void Validate(string providerName)
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new JevGenException(
                $"The '{providerName}' provider is configured with a non-positive timeout of {Timeout}.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new EvaluationAuthenticationException(
                $"No API key is configured for the '{providerName}' provider. Set it through " +
                "configuration, an environment variable or user secrets.")
            {
                Provider = providerName,
            };
        }
    }
}

/// <summary>Well-known Jev model aliases.</summary>
/// <remarks>
/// <para>
/// An alias is resolved by each provider into the identifier that provider actually uses, so
/// the same contract runs unchanged against TypeSafe, OpenRouter or a gateway.
/// </para>
/// <para>
/// There is one, because Jev has one generally available model. Any other identifier is passed
/// to the host unchanged, which is how a specific build is pinned:
/// <c>options.Model = "typesafe/jev-1.13-20260917"</c>.
/// </para>
/// </remarks>
public static class JevModel
{
    /// <summary>The newest generally available Jev model.</summary>
    public const string Latest = "jev-latest";
}
