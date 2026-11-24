using GenWave.Core.Abstractions;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Never mention what is finished. Offers the DJ the claims they have just let go of, as a "Never mention:" list, so they do not bring a
/// finished topic back (F218.19). Shaping only: where <see cref="MemoryBreakGate"/> opens memory, the note is the list from
/// <see cref="IMemoryRecallSource.NeverMentionAsync"/> as <see cref="LlmPromptBuilder.BuildMemoryBlocks"/> builds it for the legacy persona section too.
/// An empty list is no note. It marks nothing and keeps no state. A fault is not swallowed here: the rule book reports it as "Could not shape" (F220.9).
/// </summary>
public sealed class NeverMentionRule(IMemoryRecallSource recall, IMemoryBreakGate gate) : IShapingTalkRule
{
    public string Id => "never-mention";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Never mention finished topics.",
        "Gives the DJ a short list of things they let go of in the last week (up to ten, newest first) and tells them never to mention them. It adds nothing when there is nothing on the list. It only works while the DJ's memory is on.",
        "A DJ who keeps bringing up something they decided to forget sounds like they cannot listen to themselves.",
        "The DJ let go of the story about the transmitter failing, so the prompt lists it and the next lead-in leaves it out.");

    public async ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct)
    {
        if (await gate.OpenAsync(before, ct) is not { } memoryBreak)
            return null;

        var packet = await recall.NeverMentionAsync(memoryBreak.Speaker.PersonaId, memoryBreak.Budget, ct);
        var block = LlmPromptBuilder.BuildMemoryBlocks(packet);
        return block.Count == 0 ? null : new RuleShaping(string.Join('\n', block), null);
    }
}
