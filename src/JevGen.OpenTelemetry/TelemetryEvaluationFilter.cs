using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JevGen.Telemetry;

/// <summary>
/// Instruments evaluations as they pass through the runtime pipeline.
/// </summary>
/// <remarks>
/// The filter sits outside the provider call, so it sees the whole evaluation including retries
/// and fallbacks, and it needs no cooperation from generated clients.
/// </remarks>
public sealed class TelemetryEvaluationFilter(
    IOptionsMonitor<JevGenTelemetryOptions> options,
    ILogger<TelemetryEvaluationFilter>? logger = null) : IEvaluationFilter
{
    private readonly ILogger _logger =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TelemetryEvaluationFilter>.Instance;

    /// <summary>Telemetry runs outermost so it observes everything the pipeline does.</summary>
    public int Order => int.MinValue;

    /// <inheritdoc />
    public async ValueTask<EvaluationResponse> InvokeAsync(
        EvaluationContext context,
        EvaluationDelegate next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var settings = options.CurrentValue;
        var request = context.Request;

        using var activity = JevGenTelemetry.ActivitySource.StartActivity(
            JevGenTelemetry.Activities.Evaluate,
            ActivityKind.Client);

        var tags = new TagList
        {
            { "jevgen.contract", request.ClientName },
            { "jevgen.method", request.MethodName },
            { "jevgen.provider", context.Provider.Name },
        };

        if (activity is not null)
        {
            activity.DisplayName = $"{request.ClientName}.{request.MethodName}";
            activity.SetTag("jevgen.contract", request.ClientName);
            activity.SetTag("jevgen.method", request.MethodName);
            activity.SetTag("jevgen.provider", context.Provider.Name);
            activity.SetTag("jevgen.question_count", request.Questions.Length);
            activity.SetTag("jevgen.attempt", context.Attempt);

            if (request.Model is { } model)
            {
                activity.SetTag("jevgen.model", model);
            }

            if (request.ContractVersion is { } version)
            {
                activity.SetTag("jevgen.contract_version", version);
            }

            if (settings.RecordQuestionNames)
            {
                activity.SetTag("jevgen.questions", string.Join(",", request.Questions.Select(q => q.Id)));
            }

            if (settings.RecordPrompts)
            {
                foreach (var question in request.Questions)
                {
                    activity.SetTag($"jevgen.prompt.{question.Id}", question.Prompt);
                }
            }

            if (settings.RecordState)
            {
                // Only ever reached when the application has explicitly accepted that model
                // inputs will reach its telemetry backend.
                activity.SetTag("jevgen.state", DescribeState(request));
            }
        }

        JevGenTelemetry.Requests.Add(1, tags);
        JevGenTelemetry.QuestionCount.Record(request.Questions.Length, tags);

        if (context.Attempt > 1)
        {
            JevGenTelemetry.Retries.Add(1, tags);
            JevGenTelemetry.Fallbacks.Add(1, tags);

            using var fallback = JevGenTelemetry.ActivitySource.StartActivity(
                JevGenTelemetry.Activities.Fallback,
                ActivityKind.Internal);

            fallback?.SetTag("jevgen.attempt", context.Attempt);
            fallback?.SetTag("jevgen.provider", context.Provider.Name);
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            var response = await next(context, cancellationToken).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);

            JevGenTelemetry.Duration.Record(elapsed.TotalSeconds, tags);

            if (settings.RecordConfidence)
            {
                foreach (var result in response.Results.Values)
                {
                    JevGenTelemetry.Confidence.Record(ConfidenceOf(result), tags);
                }
            }

            if (activity is not null)
            {
                activity.SetTag("jevgen.result_model", response.Metadata.Model);

                if (settings.RecordRequestIds && response.Metadata.RequestId is { } requestId)
                {
                    activity.SetTag("jevgen.request_id", requestId);
                }

                if (settings.RecordAnswers)
                {
                    foreach (var (questionId, result) in response.Results)
                    {
                        activity.SetTag($"jevgen.answer.{questionId}", Describe(result));
                    }
                }

                activity.SetStatus(ActivityStatusCode.Ok);
            }

            _logger.EvaluationSucceeded(
                request.ClientName,
                request.MethodName,
                context.Provider.Name,
                elapsed.TotalMilliseconds);

            return response;
        }
        catch (Exception exception)
        {
            tags.Add("jevgen.error_type", exception.GetType().Name);
            JevGenTelemetry.Errors.Add(1, tags);
            JevGenTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);

            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);

            _logger.EvaluationFailed(
                exception,
                request.ClientName,
                request.MethodName,
                context.Provider.Name);

            throw;
        }
    }

    private static double ConfidenceOf(JevQuestionResult result) => result switch
    {
        NoulQuestionResult noul => Math.Max(noul.Probability, 1d - noul.Probability),
        ChoiceQuestionResult choice => choice.Confidence,
        ScoreQuestionResult score => score.Confidence ?? 1d,
        _ => 1d,
    };

    private static string Describe(JevQuestionResult result) => result switch
    {
        NoulQuestionResult noul => noul.Probability.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        ChoiceQuestionResult choice => choice.SelectedOptionId,
        ScoreQuestionResult score => score.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        _ => result.GetType().Name,
    };

    private static string DescribeState(EvaluationRequest request)
    {
        try
        {
            return request.StateTypeInfo is not null
                ? System.Text.Json.JsonSerializer.Serialize(request.State, request.StateTypeInfo)
                : request.State.GetType().Name;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            return request.State.GetType().Name;
        }
    }
}

internal static partial class TelemetryLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Evaluated {Contract}.{Method} on provider {Provider} in {ElapsedMilliseconds}ms.")]
    internal static partial void EvaluationSucceeded(
        this ILogger logger,
        string contract,
        string method,
        string provider,
        double elapsedMilliseconds);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Evaluation of {Contract}.{Method} on provider {Provider} failed.")]
    internal static partial void EvaluationFailed(
        this ILogger logger,
        Exception exception,
        string contract,
        string method,
        string provider);
}
