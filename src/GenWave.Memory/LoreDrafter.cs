using GenWave.Core.Abstractions;

namespace GenWave.Memory;

/// <summary>
/// SPEC F218.20 (STORY-538) — asks the language model for five lore entries from a persona's soul and backstory.
/// It only drafts: storing the lines as pending rows is the caller's job, and nothing here logs persona text.
/// An LLM transport fault propagates; the caller owns the plain-words answer.
/// </summary>
public sealed class LoreDrafter(ILifeClaimLlm llm)
{
    public async Task<LoreDraft> DraftAsync(
        string soul, string backstory, IReadOnlyList<string> existingLore, CancellationToken ct)
    {
        var reply = await llm.CompleteAsync(LoreDraftPrompt.System, LoreDraftPrompt.User(soul, backstory, existingLore), ct);
        if (reply is null)
            return new LoreDraft(LoreDraftStatus.NotConfigured, []);

        var lines = LoreReplyParser.Parse(reply, existingLore);
        return new LoreDraft(lines.Count > 0 ? LoreDraftStatus.Drafted : LoreDraftStatus.NoUsableLines, lines);
    }
}
