using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JevGen.Extensions.AI;

/// <summary>Registers JevGen's Microsoft.Extensions.AI integration.</summary>
public static class ExtensionsAIServiceCollectionExtensions
{
    /// <summary>Adds the dynamic <see cref="IEvaluationClient"/>.</summary>
    public static JevGenBuilder AddEvaluationClient(this JevGenBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IEvaluationClient, EvaluationClient>();
        return builder;
    }
}
