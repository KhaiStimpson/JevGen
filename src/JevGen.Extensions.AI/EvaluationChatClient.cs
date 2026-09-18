using Microsoft.Extensions.AI;

namespace JevGen.Extensions.AI;

/// <summary>
/// A chat-client middleware that runs a JevGen guardrail before each request reaches the model.
/// </summary>
/// <remarks>
/// <para>
/// A Jev model is an evaluation model, not a chat model, and this package deliberately does not
/// present one as an <see cref="IChatClient"/>. Doing so would invite callers to ask it for
/// prose and to treat its probabilities as token likelihoods.
/// </para>
/// <para>
/// Instead, typed decisions are offered where they are genuinely useful in an agentic pipeline:
/// deciding whether a request should proceed, which tool to call and which model to route to.
/// </para>
/// </remarks>
public sealed class GuardrailChatClient : DelegatingChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, CancellationToken, ValueTask<NoulResult>> _shouldBlock;
    private readonly double _threshold;
    private readonly string _blockedResponse;

    /// <summary>Wraps a chat client with a guardrail evaluation.</summary>
    /// <param name="innerClient">The client to protect.</param>
    /// <param name="shouldBlock">The evaluation deciding whether a request should be blocked.</param>
    /// <param name="threshold">The probability at or above which the request is blocked.</param>
    /// <param name="blockedResponse">The message returned instead of calling the model.</param>
    public GuardrailChatClient(
        IChatClient innerClient,
        Func<IEnumerable<ChatMessage>, CancellationToken, ValueTask<NoulResult>> shouldBlock,
        double threshold = 0.5d,
        string blockedResponse = "This request was blocked by a safety policy.")
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(shouldBlock);

        if (threshold is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold), threshold, "A probability threshold must be between 0 and 1.");
        }

        _shouldBlock = shouldBlock;
        _threshold = threshold;
        _blockedResponse = blockedResponse;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var verdict = await _shouldBlock(messages, cancellationToken).ConfigureAwait(false);

        if (verdict.Value(_threshold))
        {
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, _blockedResponse))
            {
                FinishReason = ChatFinishReason.ContentFilter,
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["jevgen.blocked"] = true,
                    ["jevgen.blockProbability"] = verdict.Probability,
                },
            };
        }

        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var verdict = await _shouldBlock(messages, cancellationToken).ConfigureAwait(false);

        if (verdict.Value(_threshold))
        {
            // The guardrail decides before any tokens are produced, so nothing that should have
            // been blocked reaches the caller mid-stream.
            yield return new ChatResponseUpdate(ChatRole.Assistant, _blockedResponse)
            {
                FinishReason = ChatFinishReason.ContentFilter,
            };

            yield break;
        }

        await foreach (var update in base
            .GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }
}

/// <summary>Adds JevGen decisions to a <see cref="IChatClient"/> pipeline.</summary>
public static class ChatClientBuilderExtensions
{
    /// <summary>Blocks requests a JevGen guardrail decides should not reach the model.</summary>
    public static ChatClientBuilder UseJevGuardrail(
        this ChatClientBuilder builder,
        Func<IServiceProvider, Func<IEnumerable<ChatMessage>, CancellationToken, ValueTask<NoulResult>>> guardrail,
        double threshold = 0.5d)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(guardrail);

        return builder.Use((inner, services) =>
            new GuardrailChatClient(inner, guardrail(services), threshold));
    }
}
