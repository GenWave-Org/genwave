using GenWave.Core.Abstractions;

namespace GenWave.Memory.Tests.Fakes;

/// <summary>An extractor model that answers every call with one canned reply, optionally after a delay.</summary>
public sealed class FakeLifeClaimLlm(string? reply, Func<CancellationToken, Task>? beforeReply = null) : ILifeClaimLlm
{
    public List<string> UserPrompts { get; } = [];

    public async Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        UserPrompts.Add(userPrompt);
        if (beforeReply is not null)
            await beforeReply(ct);
        return reply;
    }
}
