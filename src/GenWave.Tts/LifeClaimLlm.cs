namespace GenWave.Tts;

using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

/// <summary>
/// <see cref="ILifeClaimLlm"/> over the same OpenAI-compatible endpoint, key and timeout the copy writer uses
/// (SPEC F218.17), read fresh per call like every <c>Llm:*</c> option (F36.2). It goes through the one shared wire
/// seam, <see cref="LlmCopyWriter.PostChatCompletionAsync"/>, and is called from the background extractor and the operator's lore suggestion (F218.20).
/// </summary>
public sealed class LifeClaimLlm(IHttpClientFactory httpClientFactory, IOptionsMonitor<LlmOptions> optionsMonitor) : ILifeClaimLlm
{
    /// <summary>Room for up to five short claims as JSON, or five lore lines.</summary>
    const int MaxTokens = 400;

    public async Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var cfg = optionsMonitor.CurrentValue;
        if (string.IsNullOrEmpty(cfg.Endpoint))
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(cfg.TimeoutSeconds));

        var http = httpClientFactory.CreateClient(LlmCopyWriter.HttpClientName);
        var requestUri = EndpointUri.Combine(cfg.Endpoint, "/v1/chat/completions");
        var reply = await LlmCopyWriter.PostChatCompletionAsync(http, requestUri, cfg, systemPrompt, userPrompt, MaxTokens, timeoutCts.Token);
        return reply.Content;
    }
}
