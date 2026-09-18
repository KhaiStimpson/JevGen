using Microsoft.Extensions.AI;

namespace JevGen.Extensions.AI;

/// <summary>
/// Puts a typed decision layer between an agent and its tools.
/// </summary>
/// <remarks>
/// Asking a general model to pick a tool buries the choice inside prose and gives no calibrated
/// measure of how sure it was. Routing through an evaluation contract makes the choice a typed
/// value with a probability distribution, so an agent can require confidence before acting and
/// fall back to asking the user when it is not there.
/// </remarks>
public static class JevToolRouter
{
    /// <summary>
    /// Selects the tool a choice result names, or <see langword="null"/> when the model was not
    /// confident enough.
    /// </summary>
    /// <param name="tools">The tools available to the agent.</param>
    /// <param name="choice">The routing decision.</param>
    /// <param name="minimumConfidence">The confidence required before a tool is invoked.</param>
    public static AITool? Select<TTool>(
        IReadOnlyList<AITool> tools,
        ChoiceResult<TTool> choice,
        double minimumConfidence = 0.6d)
        where TTool : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(tools);

        if (choice.Confidence < minimumConfidence)
        {
            return null;
        }

        var name = choice.Value.ToString();

        foreach (var tool in tools)
        {
            if (string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return tool;
            }
        }

        return null;
    }

    /// <summary>
    /// Restricts an agent's tool list to the one a routing decision selected, leaving it
    /// unchanged when confidence is too low to narrow safely.
    /// </summary>
    public static ChatOptions WithRoutedTool<TTool>(
        this ChatOptions options,
        ChoiceResult<TTool> choice,
        double minimumConfidence = 0.6d)
        where TTool : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Tools is not { Count: > 0 } tools)
        {
            return options;
        }

        var selected = Select([.. tools], choice, minimumConfidence);

        if (selected is null)
        {
            return options;
        }

        var narrowed = options.Clone();
        narrowed.Tools = [selected];
        return narrowed;
    }
}
