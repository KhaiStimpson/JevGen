using JevGen.Providers;

namespace JevGen;

/// <summary>
/// Resolves the registered name of a provider type without instantiating it.
/// </summary>
/// <remarks>
/// Provider names come from <see cref="IJevProvider.Name"/>, which is an instance member. To
/// let <c>UseProvider&lt;T&gt;()</c> work before the container is built, a provider type may
/// also declare a <c>public const string ProviderName</c> or <c>public static string
/// ProviderName</c>; otherwise the type name is used, with any <c>JevProvider</c> or
/// <c>Provider</c> suffix trimmed.
/// </remarks>
public static class JevProviderNames
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, string> Cache = new();

    /// <summary>Returns the registered name for a provider type.</summary>
    public static string Of<TProvider>()
        where TProvider : class, IJevProvider
        => Of(typeof(TProvider));

    /// <summary>Returns the registered name for a provider type.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:RequiresUnreferencedCode",
        Justification = "Only a well-known static field on the provider type itself is read, and the provider type is referenced by the caller's generic argument, so it is preserved.")]
    public static string Of(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields |
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        Type providerType)
    {
        ArgumentNullException.ThrowIfNull(providerType);

        return Cache.GetOrAdd(providerType, static type =>
        {
            if (type.GetField("ProviderName")?.GetValue(null) is string constant && constant.Length > 0)
            {
                return constant;
            }

            if (type.GetProperty("ProviderName")?.GetValue(null) is string property && property.Length > 0)
            {
                return property;
            }

            var name = type.Name;

            foreach (var suffix in new[] { "JevProvider", "Provider" })
            {
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return name[..^suffix.Length].ToLowerInvariant();
                }
            }

            return name.ToLowerInvariant();
        });
    }
}
