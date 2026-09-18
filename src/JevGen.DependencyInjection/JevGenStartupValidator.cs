using JevGen.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JevGen;

/// <summary>
/// Validates registered contracts against the providers that will run them, when the
/// application starts.
/// </summary>
/// <remarks>
/// Failing here turns an incompatible contract/provider pairing into a deployment failure
/// rather than a production surprise on the first request.
/// </remarks>
internal sealed class JevGenStartupValidator(
    IJevProviderResolver providers,
    IOptions<JevGenOptions> options,
    ILogger<JevGenStartupValidator> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var value = options.Value;

        if (!value.ValidateOnStart)
        {
            return Task.CompletedTask;
        }

        if (providers.All.Count == 0)
        {
            logger.LogWarning(
                "No JevGen provider is registered. Evaluations will fail until one is added with " +
                "AddJevProvider, AddTypeSafeJev or AddOpenRouterJev.");

            return Task.CompletedTask;
        }

        var violations = CapabilityValidator.ValidateAndThrow(JevClientRegistry.All, providers, value);

        foreach (var violation in violations)
        {
            logger.LogWarning(
                "{Contract} requires {Missing}, which provider '{Provider}' does not support. " +
                "Continuing because capability validation is configured to warn only.",
                violation.Contract, violation.Missing, violation.Provider);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
