# Errors

Every failure JevGen raises derives from `JevGenException`. The ASP.NET Core integration maps
each one onto a problem-details response whose `type` links to the matching section below.

| Exception | Status | Retried / fallback | Typical cause |
|---|---|---|---|
| `EvaluationRateLimitException` | 429 | Yes | The provider's rate limit was exhausted |
| `EvaluationTimeoutException` | 504 | Yes | A time budget was exceeded |
| `EvaluationCircuitOpenException` | 503 | Fallback | The provider's circuit breaker is open |
| `EvaluationAuthenticationException` | 503 | No | Missing or rejected credentials |
| `EvaluationCapabilityException` | 503 | Fallback | The provider cannot honour the contract |
| `EvaluationFallbackExhaustedException` | 502 | — | Every configured provider failed |
| `EvaluationResponseException` | 502 | No | The provider's answer does not satisfy the contract |
| `EvaluationProviderException` | 502 | When `IsTransient` | Any other provider failure |
| `EvaluationSerializationException` | 500 | No | State could not be serialized |
| `JevGenException` | 500 | No | Configuration errors |

Credential and capability problems are reported as 503 rather than 4xx: they are this
service's misconfiguration, not the caller's fault.

## EvaluationRateLimitException

The provider rejected the request with a rate limit. `RetryAfter` carries the provider's
requested wait when it sent one, and the resilience filter honours it. The problem-details body
reports it as `retryAfterSeconds`.

## EvaluationTimeoutException

An evaluation exceeded a time budget: `JevGenOptions.Timeout` for the whole evaluation,
`JevResilienceOptions.Timeout` for one attempt, or the provider's HTTP timeout. `Timeout`
reports which budget was exceeded, when known.

## EvaluationCircuitOpenException

The resilience filter did not call the provider because it failed
`CircuitBreakerThreshold` times in a row. The circuit lets a single probe through once
`CircuitBreakerDuration` has passed. This is transient, so the runtime falls back to the next
configured provider; `OpenUntil` says when the next probe is allowed.

## EvaluationAuthenticationException

No API key is configured, or the provider rejected it. Never retried and never failed over: it
would fail identically everywhere. Check the provider's `ApiKey` option.

## EvaluationCapabilityException

The contract requires semantics — a probability distribution, several questions in one request,
model selection — that the provider does not support. `Missing` lists them. Register a provider
that supports them, add one as a fallback, or opt into degraded behaviour explicitly with
`AllowDegradedCapabilities()`. See [provider configuration](provider-configuration.md).

## EvaluationFallbackExhaustedException

Every provider in the contract's chain failed. `AttemptedProviders` lists them in order, and
the inner exception is the last failure.

## EvaluationResponseException

The provider answered, but not in a way the contract can use: a missing answer, an option that
is not a member of the enum, a missing probability distribution the contract requires, or a
body that is not valid JSON. Deterministic, so never retried.

## EvaluationProviderException

Any other provider failure. `StatusCode`, `Provider` and `RequestId` identify it; `IsTransient`
is true for 408, 429 and 5xx responses, which are retried and failed over.

## EvaluationSerializationException

The state could not be serialized, usually because no `JsonSerializerContext` describes it in a
trimmed or Native AOT application. See [troubleshooting](troubleshooting.md#serialization-fails-at-run-time).

## JevGenException

A configuration error: no provider registered, a provider name that does not exist, a contract
with no generated client. The message says what to change.
