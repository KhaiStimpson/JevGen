using System.Collections.Immutable;
using JevGen.Providers;

namespace JevGen.Runtime.Tests;

/// <summary>A provider with fully controllable capabilities, answers and failures.</summary>
internal sealed class FakeProvider(
    string name,
    JevProviderCapabilities? capabilities = null) : IJevProvider, IJevProviderHealth
{
    public string Name { get; } = name;

    public JevProviderCapabilities Capabilities { get; set; } = capabilities
        ?? (JevProviderCapabilities.Noul
            | JevProviderCapabilities.Choice
            | JevProviderCapabilities.Score
            | JevProviderCapabilities.Probabilities
            | JevProviderCapabilities.MultiQuestion
            | JevProviderCapabilities.StructuredState
            | JevProviderCapabilities.ModelSelection);

    public int CallCount { get; private set; }

    public JevProviderRequest? LastRequest { get; private set; }

    public Exception? Failure { get; set; }

    public double Confidence { get; set; } = 0.95d;

    public string SelectedOption { get; set; } = "billing";

    public bool Healthy { get; set; } = true;

    public ValueTask<JevProviderResponse> EvaluateAsync(
        JevProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;

        if (Failure is not null)
        {
            throw Failure;
        }

        var results = ImmutableArray.CreateBuilder<JevQuestionResult>();

        foreach (var question in request.Questions)
        {
            results.Add(question.Kind switch
            {
                JevQuestionKind.Noul => new NoulQuestionResult(question.Id, Confidence),
                JevQuestionKind.Choice => new ChoiceQuestionResult(
                    question.Id,
                    SelectedOption,
                    Confidence,
                    question.Options.ToDictionary(
                        option => option.Id,
                        option => option.Id == SelectedOption ? Confidence : (1 - Confidence) / (question.Options.Length - 1),
                        StringComparer.Ordinal)),
                _ => new ScoreQuestionResult(question.Id, 3d, Confidence),
            });
        }

        return new ValueTask<JevProviderResponse>(new JevProviderResponse
        {
            Results = results.ToImmutable(),
            Metadata = new JevProviderMetadata
            {
                Provider = Name,
                Model = request.Model ?? "fake-model",
                RequestId = $"{Name}-{CallCount}",
            },
        });
    }

    public ValueTask<JevProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        => new(Healthy
            ? JevProviderHealthResult.Healthy()
            : JevProviderHealthResult.Unhealthy($"'{Name}' was configured unhealthy."));
}
