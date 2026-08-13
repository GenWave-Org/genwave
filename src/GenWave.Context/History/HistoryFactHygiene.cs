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
///
/// <para>
/// <b>F125.1 (gh-#468, T271): wind-storm nouns, no bare impact verbs.</b> A tornado touchdown aired
/// as patter color because no wind-storm noun existed — <c>tornado/tornadoes/tornados</c> (the
/// Merriam-Webster-accepted variant plural, same bug class this task exists to fix),
/// <c>hurricane/hurricanes</c>, <c>cyclone/cyclones</c>, <c>typhoon/typhoons</c>, and
/// <c>blizzard/blizzards</c> closed that specific gap. gh-#468's own fix-shape also proposed four
/// impact verbs — <c>devastated</c>, <c>destroyed</c>, <c>leveled</c>, and <c>struck</c> — and all
/// four were weighed and rejected as a set, not just <c>struck</c>: each is a general-purpose
/// intensity verb outside this gate's own "violent-death / disaster / atrocity" vocabulary charter
/// above, and each collides with ordinary On-This-Day fact prose — sporting blowouts ("devastated
/// their rivals 6-0"), a stadium/library "destroyed the record" or "destroyed in a fire" (the fire
/// case already caught by <c>fire</c> itself), impeachments/indictments ("charges were leveled
/// against…"), and treaties/coinage/discoveries ("a trade agreement was struck…") alike. With the
/// gate already removing 33–46% of a day's facts and no noun-based redundancy to fall back on for
/// these verbs, the finite pool doesn't afford that collision surface — and the gh-#468 sighting
/// itself carried no impact verb at all; the nouns are the coverage. The <c>events</c>-feed widening
/// above is the correct lever if coverage keeps thinning; verb widening isn't.
/// </para>
///
/// <para>
/// <b>gh-#479: the other 13 disaster nouns had the same singular-only gap.</b> T271 gave every
/// wind-storm noun its plural but left the pre-existing vocabulary singular-only — "two
/// earthquakes struck…" passed the gate. <c>earthquake</c>, <c>tsunami</c>, <c>wildfire</c>,
/// <c>avalanche</c>, <c>landslide</c>, <c>mudslide</c>, <c>epidemic</c>, <c>eruption</c>,
/// <c>massacre</c>, <c>shipwreck</c>, <c>famine</c>, <c>genocide</c>, and <c>plague</c> all
/// pluralize with a bare <c>-s</c> (no irregular form like <c>tornado/tornadoes</c>), so each
/// gained one explicit plural beside its singular — same idiom as F125.1, no new posture.
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
        @"\b(?:assassinated|assassination|assassinations|avalanche|avalanches|blizzard|blizzards" +
        @"|bombed|bombing|bombings|bomb|bombs|casualties|collided|collision|collisions|crash|crashed" +
        @"|crashes|cyclone|cyclones|dead|death|deaths|derailed|derailment|died|dies|disaster" +
        @"|disasters|drowned|earthquake|earthquakes|epidemic|epidemics|eruption|eruptions|executed" +
        @"|execution|executions|exploded|explosion|famine|famines|fatal|fatalities|fatally|fire|fires" +
        @"|flood|flooding|floods|genocide|genocides|hanged|hijack|hijacked|hijacking|hostage|hostages" +
        @"|hurricane|hurricanes|invaded|invasion|killed|killing|kills|landslide|landslides|lynching" +
        @"|massacre|massacres|mudslide|mudslides|murder|murdered|murders|pandemic|perished|plague" +
        @"|plagues|raid|raided|raids|riot|riots|sank|shipwreck|shipwrecks|shooting|shootings|shot" +
        @"|slain|suicide|terror|terrorism|terrorist|terrorists|tornado|tornadoes|tornados|tsunami" +
        @"|tsunamis|typhoon|typhoons|war|warfare|wars|wildfire|wildfires|wounded)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SomberTermRx();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RepeatedWhitespaceRx();
}
