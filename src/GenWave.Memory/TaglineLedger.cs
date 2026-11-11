using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Memory;

/// <summary>
/// Default <see cref="ITaglineLedger"/> (SPEC F218.30–.31). Pure. A use is the T691 rule: a contiguous, in-order
/// run of min(<see cref="RepetitionCheck.QuirkRunWords"/>, the tagline's word count) key words in an utterance.
/// Unless a card's rule overrides it, a tagline is unavailable after <see cref="DefaultMaxPerHour"/> uses in the
/// rolling <see cref="Window"/> or when one of the persona's newest <see cref="DefaultMinBreaksApart"/>
/// utterances used it. A spent <c>maxPerShift</c> quirk keeps F218.7: its spans stay unavailable.
/// </summary>
public sealed class TaglineLedger : ITaglineLedger
{
    /// <summary>Uses allowed per rolling <see cref="Window"/> when the card sets no <c>maxPerHour</c>.</summary>
    public const int DefaultMaxPerHour = 2;

    /// <summary>Newest utterances that must not use a tagline when the card sets no <c>minBreaksApart</c>.</summary>
    public const int DefaultMinBreaksApart = 3;

    /// <summary>The rolling window the hourly quota counts over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    const int MinTaglineWords = 2;

    sealed record Tagline(string Span, string? QuirkText, QuirkRule? Rule);

    public IReadOnlyList<TaglineStatus> Evaluate(
        PersonaCard card,
        IReadOnlyList<UtteranceEntry> recent,
        IReadOnlyList<string> spentShiftQuirks,
        DateTimeOffset now)
    {
        var uses = recent
            .Where(u => !u.Fallback)
            .OrderByDescending(u => u.WrittenAt)
            .ThenByDescending(u => u.Id)
            .Select(u => (u.WrittenAt, Words: RepetitionText.Tokens(u.Text)))
            .ToList();

        return Taglines(card)
            .Select(t => new TaglineStatus(t.Span, IsAvailable(t, uses, spentShiftQuirks, now), t.QuirkText))
            .ToList();
    }

    static bool IsAvailable(
        Tagline tagline,
        List<(DateTimeOffset WrittenAt, string[] Words)> newestFirst,
        IReadOnlyList<string> spentShiftQuirks,
        DateTimeOffset now)
    {
        if (tagline.Rule?.MaxPerShift is not null && tagline.QuirkText is { } text && spentShiftQuirks.Contains(text, StringComparer.Ordinal))
            return false;

        var key = RepetitionText.Tokens(tagline.Span);
        var used = newestFirst.Select(u => RepetitionCheck.ContainsKeyRun(u.Words, key)).ToList();

        var inWindow = newestFirst.Where((u, i) => used[i] && u.WrittenAt > now - Window).Count();
        if (inWindow >= (tagline.Rule?.MaxPerHour ?? DefaultMaxPerHour))
            return false;

        return !used.Take(tagline.Rule?.MinBreaksApart ?? DefaultMinBreaksApart).Any(u => u);
    }

    static IEnumerable<Tagline> Taglines(PersonaCard card)
    {
        foreach (var quirk in card.Quirks ?? [])
        {
            var rule = card.QuirkRules?.FirstOrDefault(r => r.Text == quirk);
            foreach (var span in RepetitionText.QuotedSpans(quirk).Where(IsTagline))
                yield return new Tagline(span, quirk, rule);
        }

        var tagline = card.Tagline?.Trim();
        if (!string.IsNullOrEmpty(tagline) && IsTagline(tagline))
            yield return new Tagline(tagline, null, null);
    }

    static bool IsTagline(string span) => RepetitionText.Tokens(span).Length >= MinTaglineWords;
}
