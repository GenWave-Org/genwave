using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.TalkRules;

namespace GenWave.Tts;

/// <summary>
/// The card phrases of a speaker, derived once for every snapshot source (F220.7: the same whatever is switched on).
/// </summary>
internal static class TalkCardPhrases
{
    /// <summary>
    /// Every tagline span of the card (a ledger read over no history lists them all, available or not) plus the
    /// card's raw quirk rule texts, deduped. Depends on the card only, never on which rules run. No ledger means no
    /// tagline spans (T713 convention).
    /// </summary>
    public static TalkList<string> Of(PersonaCard? card, ITaglineLedger? taglineLedger, DateTimeOffset localNow)
    {
        var spans = card is not null && taglineLedger is not null
            ? taglineLedger.Evaluate(card, [], [], localNow).Select(t => t.Span)
            : [];

        return new TalkList<string>(spans
            .Concat((card?.QuirkRules ?? []).Select(r => r.Text))
            .Distinct(StringComparer.Ordinal));
    }
}
