using System.Text;
using JevGen.Providers;

namespace JevGen;

/// <summary>A contract requirement the selected provider cannot satisfy.</summary>
/// <param name="Contract">The contract member, as <c>IInterface.Method</c>.</param>
/// <param name="Provider">The provider that was checked.</param>
/// <param name="Required">Everything the contract member requires.</param>
/// <param name="Missing">The subset the provider does not support.</param>
public sealed record CapabilityViolation(
    string Contract,
    string Provider,
    JevCapabilitySet Required,
    JevCapabilitySet Missing)
{
    /// <summary>Renders the violation in the form used by start-up diagnostics.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(Contract).AppendLine(" requires:");
        Append(builder, Required);
        builder.AppendLine().Append("Provider \"").Append(Provider).AppendLine("\" supports:");
        Append(builder, Required & ~Missing);
        builder.AppendLine().AppendLine("Missing:");
        Append(builder, Missing);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, JevCapabilitySet capabilities)
    {
        var any = false;

        foreach (var value in Enum.GetValues<JevCapabilitySet>())
        {
            if (value != JevCapabilitySet.None && capabilities.HasFlag(value))
            {
                builder.Append("- ").AppendLine(value.ToString());
                any = true;
            }
        }

        if (!any)
        {
            builder.AppendLine("- (none)");
        }
    }
}

/// <summary>
/// Checks registered contracts against the providers that will run them.
/// </summary>
/// <remarks>
/// This runs at start-up rather than on first call, so an incompatible pairing surfaces as a
/// deployment failure instead of a production surprise. Silent degradation is never an option:
/// the only configurable choice is whether a mismatch fails or warns.
/// </remarks>
public static class CapabilityValidator
{
    /// <summary>Returns every violation across the registered contracts.</summary>
    public static IReadOnlyList<CapabilityViolation> Validate(
        IEnumerable<JevClientDescriptor> descriptors,
        IJevProviderResolver providers,
        JevGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);

        var violations = new List<CapabilityViolation>();

        foreach (var descriptor in descriptors)
        {
            options.Clients.TryGetValue(descriptor.ContractType.FullName ?? descriptor.Name, out var clientConfiguration);

            var chain = ResolveChain(descriptor, providers, options, clientConfiguration);

            if (chain.Count == 0)
            {
                continue;
            }

            foreach (var method in descriptor.Methods)
            {
                var required = method.RequiredCapabilities;

                // A contract is satisfiable when any provider in its chain can serve it.
                var satisfied = chain.Any(provider => provider.Capabilities.Missing(required) == JevCapabilitySet.None);

                if (satisfied)
                {
                    continue;
                }

                var primary = chain[0];

                violations.Add(new CapabilityViolation(
                    $"{descriptor.Name}.{method.Name}",
                    primary.Name,
                    required,
                    primary.Capabilities.Missing(required)));
            }
        }

        return violations;
    }

    /// <summary>
    /// Validates and throws when a violation is found and the configured mode is
    /// <see cref="CapabilityValidationMode.Fail"/>.
    /// </summary>
    /// <exception cref="EvaluationCapabilityException">A contract cannot be served.</exception>
    public static IReadOnlyList<CapabilityViolation> ValidateAndThrow(
        IEnumerable<JevClientDescriptor> descriptors,
        IJevProviderResolver providers,
        JevGenOptions options)
    {
        var violations = Validate(descriptors, providers, options);

        if (violations.Count == 0 || options.CapabilityValidation != CapabilityValidationMode.Fail)
        {
            return violations;
        }

        var missing = violations.Aggregate(JevCapabilitySet.None, (current, violation) => current | violation.Missing);

        throw new EvaluationCapabilityException(
            "JevGen contracts require semantics the configured providers do not support:"
            + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => v.ToString()))
            + Environment.NewLine
            + "Register a provider that supports these semantics, configure a fallback provider that does, "
            + "or opt into degraded behaviour explicitly by setting CapabilityValidation to Warn.")
        {
            Provider = violations[0].Provider,
            Contract = violations[0].Contract,
            Missing = missing,
        };
    }

    private static List<IJevProvider> ResolveChain(
        JevClientDescriptor descriptor,
        IJevProviderResolver providers,
        JevGenOptions options,
        JevClientConfiguration? clientConfiguration)
    {
        var chain = new List<IJevProvider>();
        var primaryName = clientConfiguration?.Provider ?? descriptor.Provider ?? options.DefaultProvider;

        if (primaryName is not null)
        {
            if (providers.TryResolve(primaryName, out var named))
            {
                chain.Add(named);
            }
        }
        else if (providers.Default is { } fallback)
        {
            chain.Add(fallback);
        }

        if (clientConfiguration is not null)
        {
            foreach (var name in clientConfiguration.FallbackProviders)
            {
                if (providers.TryResolve(name, out var provider) && !chain.Contains(provider))
                {
                    chain.Add(provider);
                }
            }
        }

        return chain;
    }
}
