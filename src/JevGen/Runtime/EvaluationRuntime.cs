using System.Diagnostics;
using JevGen.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JevGen;

/// <summary>
/// The default <see cref="IEvaluationRuntime"/>.
/// </summary>
/// <remarks>
/// It resolves a provider using the documented precedence, validates the contract's semantic
/// requirements against that provider, runs the configured filter pipeline and orchestrates
/// fallback. Generated clients depend only on <see cref="IEvaluationRuntime"/>, so all of this
/// can change — and hosting can move between TypeSafe, OpenRouter, a gateway or a custom
/// provider — without regenerating or changing an application contract.
/// </remarks>
public sealed class EvaluationRuntime : IEvaluationRuntime
{
    private readonly IJevProviderResolver _providers;
    private readonly IOptionsMonitor<JevGenOptions> _options;
    private readonly ILogger<EvaluationRuntime> _logger;
    private readonly EvaluationDelegate _pipeline;

    /// <summary>Creates the runtime.</summary>
    public EvaluationRuntime(
        IJevProviderResolver providers,
        IOptionsMonitor<JevGenOptions> options,
        IEnumerable<IEvaluationFilter> filters,
        ILogger<EvaluationRuntime>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(filters);

        _providers = providers;
        _options = options;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EvaluationRuntime>.Instance;
        _pipeline = BuildPipeline([.. filters.OrderBy(f => f.Order)]);
    }

    /// <inheritdoc />
    public async ValueTask<EvaluationResponse> EvaluateAsync(
        EvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.CurrentValue;
        var clientConfiguration = FindClientConfiguration(options, request.ClientName);
        var candidates = ResolveCandidates(request, options, clientConfiguration);

        using var timeoutSource = CreateTimeoutSource(options, cancellationToken, out var effectiveToken);

        var validationMode = clientConfiguration?.CapabilityValidation ?? options.CapabilityValidation;
        var required = request.RequiredCapabilities;

        var attempted = new List<string>(candidates.Count);
        var started = Stopwatch.GetTimestamp();

        EvaluationResponse? best = null;
        Exception? lastFailure = null;
        EvaluationCapabilityException? lastCapabilityFailure = null;

        for (var index = 0; index < candidates.Count; index++)
        {
            var provider = candidates[index];
            var isLast = index == candidates.Count - 1;
            attempted.Add(provider.Name);

            var missing = provider.Capabilities.Missing(required);

            if (missing != JevCapabilitySet.None)
            {
                var failure = CapabilityFailure(request, provider, missing);

                if (validationMode == CapabilityValidationMode.Fail)
                {
                    lastCapabilityFailure = failure;

                    // An unsupported capability is a legitimate reason to fall back, but never a
                    // reason to silently answer with degraded semantics.
                    if (!isLast)
                    {
                        _logger.LogWarning(
                            "Provider '{Provider}' cannot satisfy {Contract}.{Method} (missing {Missing}); trying the next provider.",
                            provider.Name, request.ClientName, request.MethodName, missing);
                        continue;
                    }

                    throw failure;
                }

                _logger.LogWarning(
                    "Provider '{Provider}' cannot satisfy {Contract}.{Method} (missing {Missing}). " +
                    "Continuing because capability validation is configured to warn only.",
                    provider.Name, request.ClientName, request.MethodName, missing);
            }

            var context = new EvaluationContext(
                Scope(request, provider, clientConfiguration),
                provider,
                attempted.Count);

            try
            {
                var response = await _pipeline(context, effectiveToken).ConfigureAwait(false);
                response = WithRuntimeMetadata(response, attempted.Count, started, options);

                if (!ShouldFallbackOnConfidence(response, clientConfiguration, isLast))
                {
                    return response;
                }

                best = Better(best, response);

                _logger.LogInformation(
                    "Provider '{Provider}' answered {Contract}.{Method} below the configured confidence floor; trying the next provider.",
                    provider.Name, request.ClientName, request.MethodName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception) when (timeoutSource?.IsCancellationRequested == true)
            {
                throw new EvaluationTimeoutException(
                    $"The evaluation of {request.ClientName}.{request.MethodName} exceeded the configured " +
                    $"timeout of {options.Timeout}.",
                    exception)
                {
                    Provider = provider.Name,
                    Timeout = options.Timeout,
                };
            }
            catch (Exception exception) when (IsFallbackEligible(exception))
            {
                lastFailure = exception;

                if (isLast)
                {
                    break;
                }

                _logger.LogWarning(
                    exception,
                    "Provider '{Provider}' failed to answer {Contract}.{Method}; trying the next provider.",
                    provider.Name, request.ClientName, request.MethodName);
            }
        }

        if (best is not null)
        {
            // Every provider answered below the confidence floor: return the best answer rather
            // than failing, and let the caller's policy decide what to do with it.
            return best;
        }

        if (lastCapabilityFailure is not null && lastFailure is null)
        {
            throw lastCapabilityFailure;
        }

        if (lastFailure is not null)
        {
            if (attempted.Count == 1)
            {
                throw lastFailure;
            }

            throw new EvaluationFallbackExhaustedException(
                $"Every provider configured for {request.ClientName}.{request.MethodName} failed " +
                $"({string.Join(", ", attempted)}).",
                lastFailure)
            {
                AttemptedProviders = attempted,
            };
        }

        throw new JevGenException(
            $"No provider is registered that can serve {request.ClientName}.{request.MethodName}. " +
            "Register one with AddJevProvider, AddTypeSafeJev or AddOpenRouterJev.");
    }

    private static bool IsFallbackEligible(Exception exception) => exception switch
    {
        // Deterministic application and contract failures must never be retried or failed over.
        EvaluationAuthenticationException => false,
        EvaluationResponseException => false,
        EvaluationSerializationException => false,
        EvaluationCapabilityException => true,
        EvaluationProviderException provider => provider.IsTransient,
        HttpRequestException => true,
        TimeoutException => true,
        _ => false,
    };

    private static EvaluationCapabilityException CapabilityFailure(
        EvaluationRequest request,
        IJevProvider provider,
        JevCapabilitySet missing)
        => new(
            $"""
             {request.ClientName}.{request.MethodName} requires:
             {Describe(request.RequiredCapabilities)}

             Provider "{provider.Name}" supports:
             {Describe(provider.Capabilities.ToCapabilitySet())}

             Missing:
             {Describe(missing)}
             """)
        {
            Provider = provider.Name,
            Contract = $"{request.ClientName}.{request.MethodName}",
            Missing = missing,
        };

    private static string Describe(JevCapabilitySet capabilities)
    {
        if (capabilities == JevCapabilitySet.None)
        {
            return "- (none)";
        }

        var names = Enum.GetValues<JevCapabilitySet>()
            .Where(value => value != JevCapabilitySet.None && capabilities.HasFlag(value))
            .Select(value => $"- {value}");

        return string.Join(Environment.NewLine, names);
    }

    private List<IJevProvider> ResolveCandidates(
        EvaluationRequest request,
        JevGenOptions options,
        JevClientConfiguration? clientConfiguration)
    {
        var candidates = new List<IJevProvider>();

        // Selection precedence: explicit programmatic client configuration, then the method or
        // contract attribute carried on the request, then the global default.
        var primary = ResolvePrimary(request, options, clientConfiguration);

        if (primary is not null)
        {
            candidates.Add(primary);
        }

        if (clientConfiguration is not null)
        {
            foreach (var fallback in clientConfiguration.FallbackProviders)
            {
                Add(candidates, _providers.Resolve(fallback));
            }

            foreach (var fallbackType in clientConfiguration.FallbackProviderTypes)
            {
                Add(candidates, ResolveByType(fallbackType));
            }
        }

        return candidates;
    }

    private static void Add(List<IJevProvider> candidates, IJevProvider provider)
    {
        if (!candidates.Contains(provider))
        {
            candidates.Add(provider);
        }
    }

    private IJevProvider? ResolvePrimary(
        EvaluationRequest request,
        JevGenOptions options,
        JevClientConfiguration? clientConfiguration)
    {
        // Programmatic selection by type wins, then by name, then the attribute carried on the
        // request, then the global default, then whatever was registered first.
        if (clientConfiguration?.ProviderType is { } providerType)
        {
            return ResolveByType(providerType);
        }

        var name = clientConfiguration?.Provider ?? request.Provider ?? options.DefaultProvider;

        return name is not null ? _providers.Resolve(name) : _providers.Default;
    }

    private IJevProvider ResolveByType(Type providerType)
        => JevProviderNames.TryResolve(providerType, _providers, out var provider)
            ? provider
            : throw new JevGenException(
                $"No JevGen provider of type '{providerType}' is registered. Register it with " +
                $"AddJevProvider<{providerType.Name}>() before selecting it.");

    private static JevClientConfiguration? FindClientConfiguration(JevGenOptions options, string clientName)
    {
        foreach (var pair in options.Clients)
        {
            if (string.Equals(pair.Key, clientName, StringComparison.Ordinal)
                || pair.Key.EndsWith("." + clientName, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static EvaluationRequest Scope(
        EvaluationRequest request,
        IJevProvider provider,
        JevClientConfiguration? clientConfiguration)
    {
        if (clientConfiguration is null
            || !clientConfiguration.ProviderOptions.TryGetValue(provider.Name, out var programmatic)
            || programmatic.Count == 0)
        {
            return request;
        }

        // Programmatic provider options layer over the ones declared by attribute.
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (request.ProviderOptions.TryGetValue(provider.Name, out var declared))
        {
            foreach (var pair in declared)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in programmatic)
        {
            merged[pair.Key] = pair.Value;
        }

        var options = new Dictionary<string, IReadOnlyDictionary<string, object?>>(request.ProviderOptions, StringComparer.OrdinalIgnoreCase)
        {
            [provider.Name] = merged,
        };

        return request with { ProviderOptions = options };
    }

    private static bool ShouldFallbackOnConfidence(
        EvaluationResponse response,
        JevClientConfiguration? clientConfiguration,
        bool isLastCandidate)
    {
        if (isLastCandidate || clientConfiguration?.FallbackWhenConfidenceBelow is not { } floor)
        {
            return false;
        }

        return LowestConfidence(response) < floor;
    }

    private static EvaluationResponse Better(EvaluationResponse? current, EvaluationResponse candidate)
        => current is null || LowestConfidence(candidate) > LowestConfidence(current) ? candidate : current;

    private static double LowestConfidence(EvaluationResponse response)
    {
        var lowest = double.PositiveInfinity;

        foreach (var result in response.Results.Values)
        {
            var confidence = result switch
            {
                NoulQuestionResult noul => Math.Max(noul.Probability, 1d - noul.Probability),
                ChoiceQuestionResult choice => choice.Confidence,
                ScoreQuestionResult score => score.Confidence ?? 1d,
                _ => 1d,
            };

            lowest = Math.Min(lowest, confidence);
        }

        return double.IsPositiveInfinity(lowest) ? 1d : lowest;
    }

    private static EvaluationResponse WithRuntimeMetadata(
        EvaluationResponse response,
        int attempts,
        long startedTimestamp,
        JevGenOptions options)
    {
        if (!options.RecordMetadata)
        {
            return response;
        }

        return response with
        {
            Metadata = response.Metadata with
            {
                Attempts = attempts,
                Duration = Stopwatch.GetElapsedTime(startedTimestamp),
            },
        };
    }

    private CancellationTokenSource? CreateTimeoutSource(
        JevGenOptions options,
        CancellationToken cancellationToken,
        out CancellationToken effectiveToken)
    {
        if (options.Timeout <= TimeSpan.Zero || options.Timeout == Timeout.InfiniteTimeSpan)
        {
            effectiveToken = cancellationToken;
            return null;
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(options.Timeout);
        effectiveToken = source.Token;
        return source;
    }

    private static EvaluationDelegate BuildPipeline(IReadOnlyList<IEvaluationFilter> filters)
    {
        EvaluationDelegate pipeline = InvokeProviderAsync;

        for (var index = filters.Count - 1; index >= 0; index--)
        {
            var filter = filters[index];
            var next = pipeline;
            pipeline = (context, token) => filter.InvokeAsync(context, next, token);
        }

        return pipeline;
    }

    private static async ValueTask<EvaluationResponse> InvokeProviderAsync(
        EvaluationContext context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var provider = context.Provider;

        request.ProviderOptions.TryGetValue(provider.Name, out var providerOptions);

        var providerRequest = new JevProviderRequest
        {
            State = request.State,
            StateTypeInfo = request.StateTypeInfo,
            Questions = request.Questions,
            Model = request.Model,
            ClientName = request.ClientName,
            MethodName = request.MethodName,
            ProviderOptions = providerOptions ?? new Dictionary<string, object?>(),
            Metadata = request.Metadata,
        };

        var providerResponse = await provider.EvaluateAsync(providerRequest, cancellationToken).ConfigureAwait(false);

        return Map(request, provider, providerResponse);
    }

    private static EvaluationResponse Map(
        EvaluationRequest request,
        IJevProvider provider,
        JevProviderResponse response)
    {
        var results = new Dictionary<string, JevQuestionResult>(response.Results.Length, StringComparer.Ordinal);

        foreach (var result in response.Results)
        {
            results[result.QuestionId] = result;
        }

        foreach (var question in request.Questions)
        {
            if (!results.ContainsKey(question.Id))
            {
                throw new EvaluationResponseException(
                    $"Provider '{provider.Name}' returned no result for question '{question.Id}' of " +
                    $"{request.ClientName}.{request.MethodName}.")
                {
                    Provider = provider.Name,
                    RequestId = response.Metadata.RequestId,
                };
            }
        }

        return new EvaluationResponse
        {
            Results = results,
            Metadata = new EvaluationMetadata
            {
                Provider = response.Metadata.Provider,
                Model = response.Metadata.Model,
                RequestId = response.Metadata.RequestId,
                Properties = response.Metadata.Properties,
            },
        };
    }
}
