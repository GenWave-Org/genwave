namespace GenWave.Context.History;

using System.Text.RegularExpressions;

/// <summary>
/// The gh-#433 airability screen for On-This-Day facts, applied by <see cref="HistoryContextProvider"/>
/// at VEND time — never at fetch/cache time — so day files written before this gate existed (or by an
/// older binary) get exactly the same treatment as a fresh fetch: the cache stores what Wikimedia said,
/// the gate decides what a DJ may say.
///
/// <para>
/// <b>Two screens, one live sighting each (2026-08-09, demo box).</b>
/// <see cref="CleanMarkupResidue"/> strips wiki-markup leftovers — the curated feed's <c>text</c> can
/// carry half-stripped <c>[[wikilink]]</c> brackets ("Voepass Linhas Aéreas Flight 2283] crashed …"),
/// which a small model then reproduces or trips over. <see cref="IsSomber"/> is the tone gate: the same
/// live fact put a fatal plane crash in a chill-morning DJ's mouth ("62 people fell from the sky…").
/// Wikimedia's <c>selected</c> feed skews heavily toward disasters — the real reply captured at T228
/// build time was five-for-five somber — so a station of GenWave's temperament needs an explicit
/// screen, not curation trust.
/// </para>
///
/// <para>
/// <b>Deliberately fail-closed keyword matching.</b> A word-boundary screen over violent-death /
/// disaster / atrocity vocabulary, chosen over anything cleverer because its failure modes are
/// asymmetric: a false POSITIVE costs one benign fact (others remain; an all-somber day is a legal
/// F107.6 skip — the segment simply doesn't air), while a false NEGATIVE airs a body count on a music
/// station. "Star Wars premiered" being filtered by <c>wars</c> is an accepted loss under that
/// asymmetry. If the gate thins coverage too far in practice, the recorded follow-up (gh-#433) is
/// widening the pool by switching the provider to the fuller <c>events</c> feed and filtering from
/// ~60 entries instead of ~20 — a curation-posture change that needs its own ruling, not a bigger
/// word list.
/// </para>
/// </summary>
static partial class HistoryFactHygiene
{
    static readonly char[] BracketChars = ['[', ']'];

    /// <summary>Removes wiki-markup bracket residue and re-collapses any whitespace the removal
    /// exposed. Facts have no legitimate on-air use for square brackets — TTS cannot speak them and
    /// the LLM prompt fences don't use them — so stripping ALL of them is safe, not just provably
    /// unbalanced ones.</summary>
    public static string CleanMarkupResidue(string text)
    {
        if (text.IndexOfAny(BracketChars) < 0)
            return text;

        var stripped = text.Replace("[", string.Empty).Replace("]", string.Empty);
        return RepeatedWhitespaceRx().Replace(stripped, " ").Trim();
    }

    /// <summary>Whether the fact trips the tone gate — violent death, disaster, or atrocity
    /// vocabulary that has no place in station patter (see this class's own remarks for the
    /// fail-closed matching posture).</summary>
    public static bool IsSomber(string text) => SomberTermRx().IsMatch(text);

    [GeneratedRegex(
        @"\b(?:assassinated|assassination|assassinations|avalanche|bombed|bombing|bombings|bomb|bombs" +
        @"|casualties|collided|collision|collisions|crash|crashed|crashes|dead|death|deaths|derailed" +
        @"|derailment|died|dies|disaster|disasters|drowned|earthquake|epidemic|eruption|executed" +
        @"|execution|executions|exploded|explosion|famine|fatal|fatalities|fatally|fire|fires|flood" +
        @"|flooding|floods|genocide|hanged|hijack|hijacked|hijacking|hostage|hostages|invaded|invasion" +
        @"|killed|killing|kills|landslide|lynching|massacre|mudslide|murder|murdered|murders|pandemic" +
        @"|perished|plague|raid|raided|raids|riot|riots|sank|shipwreck|shooting|shootings|shot|slain" +
        @"|suicide|terror|terrorism|terrorist|terrorists|tsunami|war|warfare|wars|wildfire|wounded)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SomberTermRx();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RepeatedWhitespaceRx();
}
