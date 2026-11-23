using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.Tts;

namespace GenWave.TalkRules;

/// <summary>
/// Catchphrase limits, the judging half. Flags a line that says a catchphrase the speaker may not say now, as the
/// last two hits of <see cref="RepetitionCheck"/> do today (<c>LlmCopyWriter.FindRepeats</c>): a capped quirk that was not offered this
/// break (the memo's <c>Spent</c>), and a tagline the ledger marks unavailable (the memo's <c>Unavailable</c>).
/// <list type="bullet">
/// <item>Reads only its own <see cref="CatchphraseLimitsMemo"/> (F220.8). No memo, or a preview render, means nothing is judged:
/// a missing memo reads as "nothing offered" and nothing is spent (F220.9).</item>
/// <item>The hits come from the shared run with no recent lines, so only the quirk and tagline halves can fire; the repeat half is <c>repeat-phrases</c>.</item>
/// <item>A catchphrase is said where the line holds a contiguous run of <see cref="RepetitionCheck.KeyRunWords"/> of its words, the rule the ledger counts by.</item>
/// <item>No difference from today: legacy runs this check on air only and so does this rule.</item>
/// </list>
/// The shaper that picks quirks and spends them is a later task (T796).
/// </summary>
public sealed class CatchphraseLimitsRule(IRepetitionCheck sharedRun) : IJudgingTalkRule
{
    public string Id => CatchphraseLimitsMemo.RuleId;

    public TalkKinds CanCover => TalkKinds.DjLine;

    public TalkKinds OnByDefault => TalkKinds.DjLine;

    public TalkRuleDescription Describe() => new(
        "Catchphrase limits.",
        $"Flags a line that says a catchphrase the speaker may not say right now: a quirk with a limit per shift that was not offered for this line, or a tagline that is used up. Unless the card sets its own limits, a tagline is used up after {TaglineLedger.DefaultMaxPerHour} uses in an hour, or when one of the speaker's last {TaglineLedger.DefaultMinBreaksApart} lines used it. A catchphrase counts as said when the line holds a run of its words in a row (at least {RepetitionCheck.QuirkRunWords} words, the whole phrase if it is shorter, or half of a long phrase). For a quirk, the phrase is its quoted words, or its last four words when nothing is quoted. A preview is not judged.",
        "A catchphrase said every break stops being a catchphrase and becomes a tic.",
        "The speaker's tagline was said two lines ago, and the new line says it again.");

    public RuleFlag? Judge(TalkSnapshot after)
    {
        if (after.Preview || after.Kind != TalkKinds.DjLine || after.MemoOf(this) is not { } stored)
            return null;

        var memo = stored.Deserialize<CatchphraseLimitsMemo>(TalkSnapshotJson.Options);
        if (memo is null)
            return null;

        var context = new RepetitionContext([], memo.Spent, UnavailablePhrases: memo.Unavailable);
        var said = sharedRun.Check(RuleText.Of(after), context).Hits.Select(hit => hit.Phrase).Distinct(StringComparer.Ordinal).ToList();

        return said.Count == 0
            ? null
            : new RuleFlag(
                "The line says a catchphrase the speaker may not say right now.",
                $"Leave out {string.Join(" and ", said.Select(phrase => $"\"{phrase}\""))}.",
                string.Join(", ", said));
    }
}
