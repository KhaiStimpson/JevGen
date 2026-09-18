using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JevGen;

/// <summary>
/// Adds retry, per-attempt timeout and a per-provider circuit breaker to evaluations.
/// </summary>
/// <remarks>
/// <para>
/// Resilience lives in the runtime pipeline rather than in generated clients, which is what
/// lets it be added, tuned or removed without regenerating anything. It also means it applies
/// uniformly across every provider, including custom ones that do not use an
/// <see cref="HttpClient"/> and so cannot be covered by HTTP-level resilience handlers.
/// </para>
/// <para>
/// Applications already using <c>Microsoft.Extensions.Http.Resilience</c> on a provider's
/// <see cref="HttpClient"/> can leave this off and keep resilience at the transport.
/// </para>
/// </remarks>
public sealed class ResilienceEvaluationFilter(
    IOptionsMonitor<JevResilienceOptions> options,
    ILogger<ResilienceEvaluationFilter>? logger = null) : IEvaluationFilter
{
    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger _logger =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ResilienceEvaluationFilter>.Instance;

    /// <summary>Runs inside telemetry so retries are visible, and outside the provider call.</summary>
    public int Order => 100;

    /// <inheritdoc />
    public async ValueTask<EvaluationResponse> InvokeAsync(
        EvaluationContext context,
        EvaluationDelegate next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var settings = options.CurrentValue;
        var provider = context.Provider.Name;
        var circuit = _circuits.GetOrAdd(provider, static _ => new CircuitState());

        if (settings.CircuitBreakerThreshold > 0 && circuit.IsOpen(settings, out var opensAt))
        {
            throw new EvaluationProviderException(
                $"The circuit for provider '{provider}' is open until {opensAt:O} after " +
                $"{settings.CircuitBreakerThreshold} consecutive failures.")
            {
                Provider = provider,
            };
        }

        Exception? lastFailure = null;

        for (var attempt = 0; attempt <= settings.MaxRetries; attempt++)
        {
            using var attemptTimeout = CreateTimeout(settings, cancellationToken, out var attemptToken);

            try
            {
                var response = await next(context, attemptToken).ConfigureAwait(false);
                circuit.RecordSuccess();
                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception) when (attemptTimeout?.IsCancellationRequested == true)
            {
                lastFailure = new EvaluationTimeoutException(
                    $"Provider '{provider}' exceeded the per-attempt timeout of {settings.Timeout}.",
                    exception)
                {
                    Provider = provider,
                    Timeout = settings.Timeout,
                };
            }
            catch (Exception exception) when (settings.ShouldRetry(exception))
            {
                lastFailure = exception;
            }
            catch (Exception exception)
            {
                // Not retryable: fail immediately, but still count it against the circuit only
                // when it reflects provider health rather than a caller mistake.
                if (exception is EvaluationProviderException)
                {
                    circuit.RecordFailure(settings);
                }

                throw;
            }

            circuit.RecordFailure(settings);

            if (attempt == settings.MaxRetries)
            {
                break;
            }

            var delay = ComputeDelay(settings, attempt, lastFailure);

            _logger.LogDebug(
                "Retrying {Contract}.{Method} on provider {Provider} in {Delay} (attempt {Attempt} of {MaxRetries}).",
                context.Request.ClientName, context.Request.MethodName, provider, delay, attempt + 1, settings.MaxRetries);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            context.RecordRetry();
        }

        throw lastFailure ?? new EvaluationProviderException($"Provider '{provider}' failed.") { Provider = provider };
    }

    private static TimeSpan ComputeDelay(JevResilienceOptions settings, int attempt, Exception? failure)
    {
        // A provider that told us how long to wait knows better than any backoff curve.
        if (failure is EvaluationRateLimitException { RetryAfter: { } retryAfter } && retryAfter > TimeSpan.Zero)
        {
            return retryAfter <= settings.MaxDelay ? retryAfter : settings.MaxDelay;
        }

        var exponential = settings.BaseDelay * Math.Pow(2, attempt);

        if (exponential > settings.MaxDelay)
        {
            exponential = settings.MaxDelay;
        }

        if (!settings.UseJitter)
        {
            return exponential;
        }

        // Full jitter: spread retries across the whole window so concurrent callers recovering
        // from one outage do not re-create it.
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * exponential.TotalMilliseconds);
    }

    private static CancellationTokenSource? CreateTimeout(
        JevResilienceOptions settings,
        CancellationToken cancellationToken,
        out CancellationToken attemptToken)
    {
        if (settings.Timeout <= TimeSpan.Zero || settings.Timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            attemptToken = cancellationToken;
            return null;
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(settings.Timeout);
        attemptToken = source.Token;
        return source;
    }

    private sealed class CircuitState
    {
        private int _consecutiveFailures;
        private long _openedAtTicks;

        internal bool IsOpen(JevResilienceOptions settings, out DateTimeOffset opensAt)
        {
            var openedAt = Interlocked.Read(ref _openedAtTicks);

            if (openedAt == 0)
            {
                opensAt = default;
                return false;
            }

            opensAt = new DateTimeOffset(openedAt, TimeSpan.Zero) + settings.CircuitBreakerDuration;

            if (DateTimeOffset.UtcNow < opensAt)
            {
                return true;
            }

            // Half-open: let one request through to probe whether the provider has recovered.
            Interlocked.Exchange(ref _openedAtTicks, 0);
            Interlocked.Exchange(ref _consecutiveFailures, settings.CircuitBreakerThreshold - 1);
            return false;
        }

        internal void RecordSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _openedAtTicks, 0);
        }

        internal void RecordFailure(JevResilienceOptions settings)
        {
            var failures = Interlocked.Increment(ref _consecutiveFailures);

            if (settings.CircuitBreakerThreshold > 0 && failures >= settings.CircuitBreakerThreshold)
            {
                Interlocked.CompareExchange(ref _openedAtTicks, DateTimeOffset.UtcNow.UtcTicks, 0);
            }
        }
    }
}
