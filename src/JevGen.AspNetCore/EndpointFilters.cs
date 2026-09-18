using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JevGen.AspNetCore;

/// <summary>
/// Turns JevGen failures raised inside an endpoint into problem-details responses.
/// </summary>
public sealed class JevGenExceptionFilter(bool includeDetail = false) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (JevGenException exception)
        {
            return JevProblemDetails.ToResult(exception, includeDetail);
        }
    }
}

/// <summary>
/// Rejects results the model is not confident enough about, before they reach a caller.
/// </summary>
/// <remarks>
/// The gate refuses to answer; it does not quietly substitute a different one. A caller gets
/// an explicit "not confident enough" response rather than a low-confidence answer dressed up
/// as a certain one.
/// </remarks>
public sealed class ConfidenceGateFilter(double minimumConfidence) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var result = await next(context).ConfigureAwait(false);

        if (result is IAIResult evaluation && evaluation.Confidence < minimumConfidence)
        {
            return Results.Problem(
                title: "The model was not confident enough to answer.",
                detail: $"Confidence was {evaluation.Confidence:0.###}, below the required {minimumConfidence:0.###}.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["confidence"] = evaluation.Confidence,
                    ["requiredConfidence"] = minimumConfidence,
                });
        }

        return result;
    }
}

/// <summary>Adds JevGen behaviour to minimal-API endpoints.</summary>
public static class JevGenEndpointExtensions
{
    /// <summary>Maps JevGen failures in this endpoint onto problem-details responses.</summary>
    /// <param name="builder">The endpoint being configured.</param>
    /// <param name="includeDetail">
    /// Whether to include the exception message. Leave it off outside development: messages can
    /// name internal endpoints and configuration.
    /// </param>
    public static RouteHandlerBuilder WithJevProblemDetails(
        this RouteHandlerBuilder builder,
        bool includeDetail = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddEndpointFilter(new JevGenExceptionFilter(includeDetail));
    }

    /// <summary>Refuses to return a result the model is not confident enough about.</summary>
    public static RouteHandlerBuilder RequireConfidence(this RouteHandlerBuilder builder, double minimum)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (minimum is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum), minimum, "A confidence threshold must be between 0 and 1.");
        }

        return builder.AddEndpointFilter(new ConfidenceGateFilter(minimum));
    }

    /// <summary>Maps JevGen failures in this route group onto problem-details responses.</summary>
    public static RouteGroupBuilder WithJevProblemDetails(this RouteGroupBuilder builder, bool includeDetail = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter(new JevGenExceptionFilter(includeDetail));
        return builder;
    }
}
