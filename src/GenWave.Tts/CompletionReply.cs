namespace GenWave.Tts;

/// <summary>
/// One parsed <c>/v1/chat/completions</c> reply as <see cref="LlmCopyWriter"/> reads it (gh-#620):
/// the answer text plus the two fields that tell a reasoning-only reply apart from a genuinely empty
/// one — <c>finish_reason</c> and the length of the thinking model's <c>message.reasoning</c>.
/// </summary>
/// <param name="Content">The reply's <c>message.content</c>; empty when the endpoint sent none.</param>
/// <param name="FinishReason">The reply's <c>finish_reason</c> (<c>"stop"</c>, <c>"length"</c>, or null from an endpoint that predates it).</param>
/// <param name="ReasoningChars">Length of <c>message.reasoning</c> — non-zero only for a thinking-capable model that thought.</param>
internal sealed record CompletionReply(string Content, string? FinishReason, int ReasoningChars)
{
    /// <summary>
    /// The gh-#620 failure shape exactly: no answer, but the model DID produce reasoning — its
    /// chain-of-thought spent the <c>max_tokens</c> budget before a single answer token. Distinct from
    /// an empty reply with no reasoning (a model that simply said nothing), which stays the plain
    /// hygiene reject it always was.
    /// </summary>
    public bool IsReasoningOnly => string.IsNullOrWhiteSpace(Content) && ReasoningChars > 0;
}
