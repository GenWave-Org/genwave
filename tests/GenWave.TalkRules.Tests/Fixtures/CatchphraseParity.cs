using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;
using GenWave.Memory;
using GenWave.Tts;

namespace GenWave.TalkRules.Tests.Fixtures;

/// <summary>
/// Runs catchphrase fixtures through today's repeat check (<c>LlmCopyWriter.FindRepeats</c>) and through the rule on a snapshot
/// the real source built from a real quirk selection and tagline ledger result for the same card and rows.
/// </summary>
internal static class CatchphraseParity
{
    const long HostId = RepeatParity.HostId;
    const string PersonaName = "Flip";

    static readonly DateTimeOffset Now = TalkSnapshots.DjLine(string.Empty).LocalNow;
    static readonly TaglineLedger Ledger = new();

    /// <summary>
    /// The phrases today's check names for each case that come from the quirk and tagline hits, then the rule's.
    /// Hits the shared run names with those two halves switched off (every tagline available, nothing spent) are dropped from today's side.
    /// </summary>
    public static (List<string> Legacy, List<string> Rule) Verdicts(IJudgingTalkRule rule, IReadOnlyList<CatchphraseParityCase> cases) =>
        ([.. cases.Select(LegacyNamed)], [.. cases.Select(c => rule.Judge(Snapshot(c))?.Evidence ?? string.Empty)]);

    public static TalkSnapshot Snapshot(CatchphraseParityCase c, bool preview = false, bool withMemo = true)
    {
        var card = CardOf(c);
        var rows = RepeatParity.Rows(HostId, c.Recent);
        var selection = SelectionOf(c);
        var legacy = withMemo
            ? LegacyCatchphraseResult.From(card, selection, Ledger.Evaluate(card, rows.Take(10).ToList(), LlmCopyWriter.CappedNotSelectedRules(card, selection), Now))
            : null;
        var input = new DjLineSnapshotInput(
            Request(), 1, c.Copy, HostId, PersonaName, card, null, null, "America/Edmonton",
            [.. rows.Take(10)], rows, [], legacy, preview);
        return new DjLineSnapshotSource(new FixedAudiencePosture(), Ledger).BuildAsync(input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
    }

    static string LegacyNamed(CatchphraseParityCase c)
    {
        var card = CardOf(c);
        var newest = RepeatParity.Rows(HostId, c.Recent).Take(10).ToList();
        var selection = SelectionOf(c);
        var taglines = Ledger.Evaluate(card, newest, LlmCopyWriter.CappedNotSelectedRules(card, selection), Now);
        var sharedRun = LlmCopyWriter.FindRepeats(
            new RepetitionCheck(), c.Copy, Request(), PersonaName, newest, card with { QuirkRules = null }, QuirkSelection.None, [.. taglines.Select(t => t with { Available = true })]);
        var hits = LlmCopyWriter.FindRepeats(new RepetitionCheck(), c.Copy, Request(), PersonaName, newest, card, selection, taglines)
            .Where(phrase => !sharedRun.Contains(phrase));
        return string.Join(", ", hits.Distinct(StringComparer.Ordinal));
    }

    static QuirkSelection SelectionOf(CatchphraseParityCase c) =>
        c.CappedQuirk is { } quirk && c.CappedQuirkOffered ? new QuirkSelection([quirk], [quirk]) : QuirkSelection.None;

    static SegmentRequest Request() => new(SegmentKind.LeadIn, "af_heart", "GenWave", null, Now, "test-station");

    static PersonaCard CardOf(CatchphraseParityCase c) =>
        new(PersonaCard.CurrentSchemaVersion, PersonaName, c.Tagline, "", c.CappedQuirk is { } quirk ? [quirk] : [], new VoiceSpec("", "af_nova", 1.0, "en"), 0, [], [])
        {
            QuirkRules = c.CappedQuirk is { } capped ? [new QuirkRule(capped, MaxPerShift: 1)] : null,
        };
}
