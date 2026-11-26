using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Varies taste notes. Shaping only, and pure: it keeps no state. When this break's notes (<see cref="TalkSnapshot.TasteNotes"/>) overlap the notes
/// the last on-air break carried (<see cref="TalkSnapshot.PreviousTasteNotes"/>), the note is <see cref="LlmPromptBuilder.TasteRepeatNote"/>, the
/// sentence the legacy user prompt appends to the taste line too (F83.1).
/// <list type="bullet">
/// <item>The writer owns the last-break memory: it fills <c>PreviousTasteNotes</c> from the field it already keeps, so the legacy timing (replaced
/// only once a reply returns, so a failed call does not count) is unchanged.</item>
/// <item>No previous notes, or none that overlap, is no note. A preview and an announcement are left out, as legacy leaves them out.</item>
/// <item>A switched-off rule is never called, so it adds nothing and remembers nothing (F220.10).</item>
/// </list>
/// </summary>
public sealed class VaryTasteRule : IShapingTalkRule
{
    public string Id => "vary-taste";

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Varies taste notes",
        "When the song was picked for a taste the DJ already mentioned on the last break, asks the DJ to word it differently or leave it out. A preview and an owner's announcement are left out, and a new taste starts afresh.",
        "The same remark about the DJ's taste, break after break, sounds like a script.",
        "Two lead-ins in a row are for songs picked for the same favourite artist, so the second is told to vary it.");

    public ValueTask<RuleShaping?> ShapeAsync(TalkSnapshot before, CancellationToken ct)
    {
        if (before.Preview || before.SegmentKind == nameof(SegmentKind.Announcement))
            return ValueTask.FromResult<RuleShaping?>(null);

        return ValueTask.FromResult<RuleShaping?>(
            before.TasteNotes is { } notes && before.PreviousTasteNotes is { } previous && notes.Any(previous.Contains)
                ? new RuleShaping(LlmPromptBuilder.TasteRepeatNote, null)
                : null);
    }
}
