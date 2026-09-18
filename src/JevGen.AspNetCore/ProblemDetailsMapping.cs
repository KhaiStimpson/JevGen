using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JevGen.AspNetCore;

/// <summary>
/// Maps JevGen failures onto RFC 9457 problem details.
/// </summary>
/// <remarks>
/// The mapping never surfaces provider credentials or evaluation state, and it distinguishes
/// the caller's fault from the upstream's: a provider outage is a 502 or 503, not a 500 that
/// implies the application is broken.
/// </remarks>
public static class JevProblemDetails
{
    /// <summary>Converts a JevGen exception into problem details.</summary>
    public static ProblemDetails Create(JevGenException exception, bool includeDetail = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var (status, title) = Classify(exception);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://github.com/KhaiStimpson/JevGen/blob/main/docs/errors.md#{exception.GetType().Name.ToLowerInvariant()}",
            Detail = includeDetail ? exception.Message : null,
        };

        if (exception is EvaluationProviderException provider)
        {
            // The provider name and request id are what makes an upstream failure traceable;
            // neither is sensitive.
            if (provider.Provider is { } name)
            {
                problem.Extensions["provider"] = name;
            }

            if (provider.RequestId is { } requestId)
            {
                problem.Extensions["requestId"] = requestId;
            }
        }

        if (exception is EvaluationRateLimitException { RetryAfter: { } retryAfter })
        {
            problem.Extensions["retryAfterSeconds"] = (int)Math.Ceiling(retryAfter.TotalSeconds);
        }

        return problem;
    }

    private static (int Status, string Title) Classify(JevGenException exception) => exception switch
    {
        EvaluationRateLimitException => (StatusCodes.Status429TooManyRequests, "The AI provider rate limit was exceeded."),
        EvaluationTimeoutException => (StatusCodes.Status504GatewayTimeout, "The AI evaluation timed out."),

        // A credential problem is this service's misconfiguration, not the caller's fault.
        EvaluationAuthenticationException => (StatusCodes.Status503ServiceUnavailable, "The AI provider is not correctly configured."),

        EvaluationCapabilityException => (StatusCodes.Status503ServiceUnavailable, "The AI provider cannot satisfy this contract."),
        EvaluationFallbackExhaustedException => (StatusCodes.Status502BadGateway, "Every configured AI provider failed."),
        EvaluationResponseException => (StatusCodes.Status502BadGateway, "The AI provider returned an unusable response."),
        EvaluationProviderException => (StatusCodes.Status502BadGateway, "The AI provider failed."),
        EvaluationSerializationException => (StatusCodes.Status500InternalServerError, "The evaluation state could not be serialized."),
        _ => (StatusCodes.Status500InternalServerError, "The AI evaluation failed."),
    };

    /// <summary>Writes a JevGen exception to the response as problem details.</summary>
    public static IResult ToResult(JevGenException exception, bool includeDetail = false)
    {
        var problem = Create(exception, includeDetail);

        return Results.Problem(
            detail: problem.Detail,
            title: problem.Title,
            statusCode: problem.Status,
            type: problem.Type,
            extensions: problem.Extensions);
    }
}
