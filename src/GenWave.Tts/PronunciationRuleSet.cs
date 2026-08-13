using System.Text.RegularExpressions;
using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

namespace GenWave.Tts;

/// <summary>
/// Immutable, precompiled collection of pronunciation rules (SPEC F97). Each rule's
/// <see cref="PronunciationRule.Pattern"/> is <see cref="Regex.Escape"/>d before compilation —
/// operator/persona-card text becomes a literal match, never an arbitrary pattern — with
/// word-boundary anchors added only where the pattern's overall edge falls on a word-character
/// boundary (so a pattern that starts or ends with a non-word character is not force-anchored
/// there), mirroring <see cref="SpeechCorrectionSet"/>'s posture (F68.5) via the shared
/// <see cref="LiteralRegexPosture"/> helper. Matching is case-insensitive and every compiled rule
/// carries a bounded match timeout so a pathological rule cannot hang the render path; a rule that
/// times out is skipped rather than allowed to fault the whole render.
///
/// <para>
/// Unlike <see cref="SpeechCorrectionSet"/>, this set does not rewrite text: <see cref="Match"/>
/// locates the <see cref="PronunciationRule.Word"/> span *within* each matched
/// <see cref="PronunciationRule.Pattern"/> occurrence, so a caller (the Kokoro adapter's markup
/// renderer, T133) can wrap that specific occurrence with <c>[word](/ipa/)</c> — the
/// pattern/word split that makes heteronyms expressible without any part-of-speech inference
/// (F97.2). No markup rendering happens here; this type is pure and reads nothing but its own
/// compiled rules.
/// </para>
///
/// <para>
/// Rules come from two sources — station settings and the active persona's card — merged by
/// <see cref="Merge"/> with every card rule ordered ahead of every station rule (SPEC F97.3,
/// F97.4) — the shared invariant <see cref="PersonaOverStationMerge"/> documents and <see
/// cref="SpeechCorrectionSet.Merge"/> also realizes: no station rule ever pre-empts a card rule,
/// not only one whose <c>(Pattern, Word)</c> is identical to a station rule's.
/// </para>
/// </summary>
public sealed class PronunciationRuleSet
{
    private readonly IReadOnlyList<(Regex Pattern, PronunciationRule Rule)> rules;

    private PronunciationRuleSet(IReadOnlyList<(Regex Pattern, PronunciationRule Rule)> rules)
    {
        this.rules = rules;
    }

    /// <summary>An empty rule set — <see cref="Match"/> finds nothing, by construction.</summary>
    public static PronunciationRuleSet Empty { get; } = new([]);

    /// <summary>
    /// The compiled rules, in the exact order <see cref="Match"/> gives them precedence. Exists so
    /// a caller that needs to answer "did my rule compile?" (T142's rule-hit counters, T144's
    /// rules API) can inspect what actually made it through <see cref="Create"/>'s filtering
    /// without re-deriving that filter itself, mirroring
    /// <see cref="SpeechCorrectionSet.Rules"/>.
    /// </summary>
    internal IEnumerable<PronunciationRule> Rules => rules.Select(rule => rule.Rule);

    /// <summary>
    /// Compiles pronunciation rules into an immutable, matchable set. A rule whose
    /// <see cref="PronunciationRule.Word"/> is blank defaults it to
    /// <see cref="PronunciationRule.Pattern"/> — the same default <see cref="PronunciationRule.Parse"/>
    /// applies — so a rule built by deserializing operator/persona-card data that omits
    /// <c>Word</c> (the always-mispronounced-name case, <c>MacLeod</c>, F97.1) compiles here too,
    /// not only when a caller happens to construct it through <see cref="PronunciationRule.Parse"/>.
    /// After that default, a rule whose <see cref="PronunciationRule.Pattern"/> or
    /// <see cref="PronunciationRule.Word"/> is still null, empty, or whitespace-only is skipped, as
    /// is a rule whose <see cref="PronunciationRule.Word"/> does not occur inside its own
    /// <see cref="PronunciationRule.Pattern"/> at all — an unlocatable word can never be annotated,
    /// so compiling it would either match nothing or (worse) silently anchor on the wrong span;
    /// skipping degrades that one rule rather than the whole set, mirroring
    /// <see cref="SpeechCorrectionSet.Create"/>'s blank-<c>From</c> posture.
    ///
    /// <para>
    /// <b>Two more skip conditions, both feeder-path safety (T133/T137 review findings):</b> a rule
    /// whose <see cref="PronunciationRule.Ipa"/> is null, empty, or whitespace-only is skipped —
    /// <see cref="PronunciationRule.Ipa"/> is declared non-nullable, but System.Text.Json does not
    /// enforce that at deserialization time, so operator/card JSON that simply omits <c>ipa</c>
    /// (<c>{"Pattern":"MacLeod","Word":"MacLeod"}</c>, entirely plausible hand-authored data) binds a
    /// literal <see langword="null"/> here; left uncompiled, <see cref="KokoroSpeechMarkup"/> would
    /// later dereference it and crash the render the first time a pause tag needs to be positioned
    /// relative to the annotation. And a rule whose <see cref="PronunciationRule.Ipa"/> contains a
    /// <c>)</c> is skipped too: <see cref="KokoroSpeechMarkup"/> composes the wire token as literal
    /// <c>[word](ipa)</c> text with no escaping mechanism of its own, and the pinned kokoro-fastapi
    /// markup parser closes the annotation at the FIRST <c>)</c> it sees — so an ipa carrying one
    /// (parenthesized optional-segment notation is legitimate IPA) would truncate the annotation
    /// early and let the remainder of the ipa spill out as spoken text on the wire. An ipa containing
    /// <c>[</c> or <c>]</c> is skipped for a related reason: kokoro-fastapi's own <c>[pause:Ns]</c>
    /// markup (gh-#116) is honored anywhere it appears in the request text, not only outside a
    /// pronunciation annotation's parens — an ipa carrying a <c>[pause:Ns]</c>-shaped substring (e.g.
    /// an imported card's <c>/mə[pause:600s]klaʊd/</c>) would splice a literal digital-silence
    /// directive onto the wire the moment that rule's annotation renders. This is a pre-existing
    /// class of risk (a card correction's replacement text can carry the same shape), not something
    /// new to pronunciation rules — but it costs one character class to close here too. All three
    /// failures are invisible in this set's own unit tests because nothing here renders a token; they
    /// surface only downstream, which is exactly why the guard belongs at the one place every rule —
    /// station or card — must pass through to ever reach a compiled match. When
    /// <see cref="PronunciationRule.Word"/> occurs more than once inside
    /// <see cref="PronunciationRule.Pattern"/>, the FIRST occurrence binds (ordinary
    /// <see cref="string.IndexOf(string, StringComparison)"/> semantics) and later occurrences are
    /// unreachable — an operator authoring <c>("the wind wind", "wind", ...)</c> gets the first
    /// "wind" annotated, never the second. A malformed rule is dropped silently from
    /// <see cref="Match"/>, but not invisibly: it never appears in <see cref="Rules"/>, so a caller
    /// can tell a rule never compiled (F97.5) without this set doing any logging of its own
    /// (F68.6). A null element in <paramref name="rules"/> itself — the same JSON-array degenerate
    /// case <see cref="SpeechCorrectionSet.Create"/> documents — is skipped the same way.
    ///
    /// <para>
    /// <b>Ipa is canonicalized HERE, before it is ever validated (T138 review findings 2+3):</b>
    /// <see cref="CanonicalizeIpa"/> trims stray whitespace, then any slash delimiters an operator
    /// pasted (some copy the bare phonemes, some copy the slash-delimited notation IPA references
    /// typically show them in — <c>"/məˈklaʊd/"</c> and <c>" məˈklaʊd "</c> and
    /// <c>" /məˈklaʊd/ "</c> all converge on the one canonical <c>"məˈklaʊd"</c>), then whitespace
    /// again (a slash-trimmed <c>"/ ipa /"</c> leaves <c>" ipa "</c> behind). Canonicalizing BEFORE
    /// the blank/bracket checks below means a rule whose Ipa canonicalizes to blank — an operator
    /// who saved bare slashes with nothing between them, <c>"/"</c>, <c>"//"</c>, <c>"///"</c> — is
    /// exactly as invalid as one that arrived blank: dropped here, surfacing in the declared-N-
    /// compiled-M WARN above (F97.5), never reaching <see cref="KokoroSpeechMarkup"/> to render a
    /// hollow <c>[word](//)</c> token on the wire. Every compiled <see cref="PronunciationMatch.Rule"/>
    /// this set ever hands out therefore already carries a canonical, slash-free Ipa — the ONE
    /// normalization site every rule passes through, so a downstream renderer can trust the shape
    /// without re-normalizing it itself (see <see cref="KokoroSpeechMarkup"/>'s own remarks).
    /// </para>
    /// </summary>
    public static PronunciationRuleSet Create(IEnumerable<PronunciationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        // The drop predicate below is PronunciationRuleValidator.IsValid itself (T144 review/design:
        // the rules API needs the SAME filter, field-named via Validate, for a write-time 400 rather
        // than a silent drop) — reused, not duplicated, so this compile-time filter and the write-path
        // 400 can never disagree about what compiles. IsValid (not Validate().Count == 0) deliberately
        // — this runs for every declared rule on every station-settings/card refresh (T144 review
        // round 2 residual #4), and Validate's own List<PronunciationRuleValidationError> allocation
        // would otherwise happen here too, for a result this call site never reads.
        var compiled = rules
            .Where(rule => rule is not null)
            .Select(rule => PronunciationRule.Parse(rule.Pattern, rule.Word, CanonicalizeIpa(rule.Ipa)))
            .Where(rule => PronunciationRuleValidator.IsValid(rule.Pattern, rule.Word, rule.Ipa))
            .Select(rule => (Pattern: CompilePattern(rule), Rule: rule))
            .ToList();

        return new PronunciationRuleSet(compiled);
    }

    /// <summary>
    /// Canonicalizes an operator/card-authored Ipa field to the bare phoneme string
    /// <see cref="KokoroSpeechMarkup"/> wraps in slashes for the wire (<c>[word](/ipa/)</c>, SPEC
    /// F96.2): trim whitespace, trim slash delimiters, trim whitespace again — see <see
    /// cref="Create"/>'s own remarks for why this runs BEFORE validation and what a canonicalize-
    /// to-blank result means. Declared non-nullable, matching <see cref="PronunciationRule.Ipa"/>'s
    /// own declared type; <see cref="string.IsNullOrEmpty(string?)"/> guards the T133/T137
    /// null-Ipa JSON-deserialization gap (a literal <see langword="null"/> bound into this
    /// non-nullable field) without ever calling <see cref="string.Trim()"/> on it — <see
    /// cref="Create"/>'s blank check right after this call is what actually drops that rule.
    ///
    /// <c>internal</c> (not <c>private</c>) so <see cref="PronunciationRuleValidator"/>'s own
    /// <c>IsValid</c>/<c>Validate</c> — the fast compile-time filter and the write-path 400 seam
    /// T144's rules API calls, respectively — canonicalize a CANDIDATE rule's Ipa exactly this same
    /// way before checking it, rather than re-deriving the trim-slash-trim sequence.
    /// </summary>
    internal static string CanonicalizeIpa(string ipa) =>
        string.IsNullOrEmpty(ipa) ? ipa : ipa.Trim().Trim('/').Trim();

    /// <summary>
    /// Builds a compiled rule set directly from the resolved <see cref="ContextPronunciationRule"/>
    /// shape <c>TtsRenderContext.Rules</c> carries (SPEC F97.6) — the mirrored, dependency-free
    /// contract type <c>GenWave.Core.Domain</c> carries because that project cannot reference
    /// <c>GenWave.Tts</c>, where this compiled matcher lives (see <see cref="ContextPronunciationRule"/>'s
    /// own remarks). The ONE seam every Kokoro-kind renderer (<see cref="KokoroTtsSynthesizer"/>,
    /// <see cref="KokoroFallbackRenderer"/>) now resolves a context's rules through, replacing what
    /// used to be an identical private conversion duplicated in each renderer (T137 review finding) —
    /// a future field added to the mirrored pair only ever needs updating here. An empty list maps
    /// straight to <see cref="Empty"/> rather than paying for <see cref="Create"/>'s own (equivalent)
    /// empty compile, so the overwhelmingly common "no rules" case allocates nothing new.
    /// </summary>
    public static PronunciationRuleSet FromContext(IReadOnlyList<ContextPronunciationRule> rules) =>
        rules.Count == 0
            ? Empty
            : Create(rules.Select(rule => new PronunciationRule(rule.Pattern, rule.Word, rule.Ipa)));

    /// <summary>
    /// Merges two rule sets: every <paramref name="card"/> rule is ordered AHEAD of every
    /// <paramref name="station"/> rule (SPEC F97.4, amending the shipped station-over-card
    /// precedence F71.7 established) — the shared invariant <see cref="PersonaOverStationMerge"/>
    /// documents in full, realized here through <see cref="Match"/>'s own first-rule-claims-the-span
    /// overlap policy: the earlier rule in set order claims the whole overlapping span and the
    /// later rule's occurrence there is dropped, not merely left "unaffected" — the two rules
    /// genuinely contend for that text, and the earlier one wins it. Ordering every card rule
    /// first therefore means no station rule ever pre-empts a card rule's occurrence (SPEC
    /// F97.3), not only one identical <c>(Pattern, Word)</c> match — but a card rule can still
    /// lose an overlapping span to ANOTHER card rule that precedes it in <paramref name="card"/>'s
    /// own order, the same as any other two contending rules in <see cref="Match"/>'s overlap
    /// policy. A station rule whose identity IS identical (case-insensitive) to a card rule's is
    /// still dropped rather than appended after it, so it can never reappear in <see
    /// cref="Rules"/> or be matched twice. Delegates to
    /// <see cref="PersonaOverStationMerge.MergeByIdentity{T}"/>, the seam <see
    /// cref="SpeechCorrectionSet.Merge"/> shares so the two can't drift apart again. An operator
    /// who needs to override a bad imported card rule edits the card, which import already made a
    /// local copy of (F90) — there is no station-side override mechanism.
    /// </summary>
    public static PronunciationRuleSet Merge(PronunciationRuleSet station, PronunciationRuleSet card)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(card);

        return new PronunciationRuleSet(
            PersonaOverStationMerge.MergeByIdentity(station.rules, card.rules, item => IdentityKey(item.Rule)));
    }

    /// <summary>
    /// Projects the SAME persona/station merge <see cref="Merge"/> encodes, but WITH per-rule
    /// provenance (SPEC F97.3, F97.4; T144's rules API) — <see cref="Merge"/> exists for matching, so
    /// it returns only the rules actually in play (a shadowed station rule is dropped entirely); this
    /// exists for DISPLAY, so it returns every compiled rule from BOTH sides, each tagged with which
    /// source supplied it and whether it is the one currently in effect. A shadowed station rule
    /// (sharing a card rule's identity) appears here with <see cref="MergedPronunciationRule.InEffect"/>
    /// <see langword="false"/> rather than vanishing — an operator staring at it needs to see WHY it
    /// is not the one firing, not have it silently disappear (STORY-254 AC2).
    ///
    /// <para>
    /// Never used for matching — <see cref="Match"/> stays pure — and <see cref="InEffect"/> is
    /// computed by calling <see cref="Merge"/> ITSELF, then checking whether EACH original entry
    /// (the exact compiled <c>(Regex, PronunciationRule)</c> pair, not merely its identity string) is
    /// still present in that output (T144 review finding F5, fixing an earlier draft that instead
    /// collected the winning IDENTITY STRINGS into a set: when a station rule is shadowed, its
    /// identity string is still present in the winning set — the card's entry sharing that same
    /// identity survived — so an identity-only check wrongly marked the shadowed station entry
    /// InEffect too; comparing the full compiled pair distinguishes "this identity survived" from
    /// "THIS entry survived"). The result is therefore never a parallel re-statement of
    /// <see cref="Merge"/>'s precedence — it IS <see cref="Merge"/>'s own compiled output, so the two
    /// can never disagree about who wins by construction. Deliberately returns the small sibling
    /// projection <see cref="MergedPronunciationRule"/> rather than widening
    /// <see cref="PronunciationRule"/> itself with a Source/InEffect field (T144 review guidance) —
    /// provenance is a fact about THIS READ, at THIS moment's merge, never a property of the rule's
    /// own stored data.
    /// </para>
    ///
    /// <para>
    /// <b>T274 review finding F3:</b> a draft of the admin preview endpoint briefly called this
    /// method as a render-time resolve seam (layering an unsaved candidate rule as this method's
    /// <c>card</c> argument) — which made "never used for matching" above false, and mislabeled the
    /// resolved station∪persona merge as <c>Source: Station</c> regardless of which side actually
    /// supplied a rule, since it was occupying this method's <c>station</c> argument position rather
    /// than either of its two REAL sources. Reverted: <see cref="PronunciationRuleResolver.ResolveForRender"/>
    /// is the render/audition seam now — this method stays what its name says, a display-purposed
    /// projection with no caller outside <c>PronunciationsController.BuildRows</c>. Do not reuse it
    /// as a resolve seam again.
    /// </para>
    /// </summary>
    public static IReadOnlyList<MergedPronunciationRule> MergeWithProvenance(
        PronunciationRuleSet station, PronunciationRuleSet card)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(card);

        var winners = Merge(station, card);
        // A HashSet, not a per-entry linear Contains over winners.rules (T144 review round 2 residual
        // #3) — O(n) membership over the combined station+card list instead of O(n²).
        var winningEntries = new HashSet<(Regex Pattern, PronunciationRule Rule)>(winners.rules);

        var cardRows = card.rules.Select(entry =>
            new MergedPronunciationRule(entry.Rule, PronunciationRuleSource.Persona, winningEntries.Contains(entry)));
        var stationRows = station.rules.Select(entry =>
            new MergedPronunciationRule(entry.Rule, PronunciationRuleSource.Station, winningEntries.Contains(entry)));

        return cardRows.Concat(stationRows).ToList();
    }

    /// <summary>
    /// Test-only seam: compiles a single rule from a raw regular expression pattern that must
    /// already carry its own <c>(?&lt;word&gt;...)</c> capture, bypassing the <see
    /// cref="Regex.Escape"/> step <see cref="Create"/> always applies to operator/card text.
    /// Exists to exercise <see cref="Match"/>'s per-rule match timeout deterministically — a
    /// pathological pattern cannot be produced through the escaped, public path. Mirrors <see
    /// cref="SpeechCorrectionSet.FromRawPattern"/>. Production code must always go through <see
    /// cref="Create"/>. Still runs <paramref name="ipa"/> through <see cref="CanonicalizeIpa"/> —
    /// <see cref="Create"/> is genuinely the ONE canonicalization site (not "the one PRODUCTION
    /// site, plus this test seam left raw"), so a raw-pattern rule's compiled Ipa is exactly as
    /// wire-ready as any other.
    /// </summary>
    internal static PronunciationRuleSet FromRawPattern(string rawPattern, string ipa)
    {
        var regex = LiteralRegexPosture.Compile($"(?<word>{rawPattern})");
        return new PronunciationRuleSet([(regex, new PronunciationRule(rawPattern, rawPattern, CanonicalizeIpa(ipa)))]);
    }

    /// <summary>
    /// A rule's merge identity (see <see cref="Merge"/>): <see cref="PronunciationRule.Pattern"/>
    /// plus <see cref="PronunciationRule.Word"/>, field-separated with <see
    /// cref="PersonaOverStationMerge.IdentityFieldSeparator"/> — the same delimiter <see
    /// cref="SpeechCorrectionSet"/>'s own identity key and <see cref="CorrectionsFingerprint"/>
    /// share.
    /// </summary>
    private static string IdentityKey(PronunciationRule rule) =>
        $"{rule.Pattern}{PersonaOverStationMerge.IdentityFieldSeparator}{rule.Word}";

    /// <summary>
    /// Locates every occurrence of every rule's <see cref="PronunciationRule.Pattern"/> in
    /// <paramref name="text"/> and reports the span of <see cref="PronunciationRule.Word"/>
    /// within each occurrence — the context that disambiguates a heteronym (F97.2).
    ///
    /// <para>
    /// <b>Overlap policy (F97.3):</b> rules are tried in set order, and when two rules' occurrences
    /// would claim overlapping spans of <paramref name="text"/>, the rule that appears earlier in
    /// the set wins the whole overlapping span — the later rule's overlapping occurrence is dropped
    /// entirely, never emitted alongside the winner. This mirrors
    /// <see cref="SpeechCorrectionSet"/>'s rewrite-in-order behaviour, where an earlier rule
    /// consumes an occurrence before a later, more general rule ever sees it — "specific rule
    /// first, general rule after" composes the same way here even though this type never rewrites
    /// text. A caller authors specific-phrase rules (<c>"wind down"</c>) before general fallbacks
    /// (<c>"wind"</c>) to get the specific reading.
    /// </para>
    ///
    /// <para>
    /// <b>Ordering guarantee:</b> the returned list is sorted ascending by <see
    /// cref="PronunciationMatch.Index"/> so a caller can walk the text once regardless of rule
    /// order; ties (which can only occur between non-overlapping matches that happen to start at
    /// the same position) are broken deterministically by set order — whichever rule's occurrence
    /// was accepted first sorts first. This ordering is independent of any particular sort
    /// algorithm's stability.
    /// </para>
    ///
    /// <para>
    /// A rule whose match times out is skipped rather than allowed to fault the whole render
    /// (mirrors F68.5).
    /// </para>
    /// </summary>
    public IReadOnlyList<PronunciationMatch> Match(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return CollectNonOverlappingMatches(text)
            .OrderBy(entry => entry.Match.Index)
            .ThenBy(entry => entry.RuleOrder)
            .Select(entry => entry.Match)
            .ToList();
    }

    /// <summary>
    /// Walks every rule's compiled pattern in set order and keeps the first occurrence to claim
    /// each span of <paramref name="text"/> — the first-rule-wins overlap policy <see
    /// cref="Match"/> documents. <c>RuleOrder</c> travels with each accepted match purely as the
    /// deterministic tiebreak <see cref="Match"/> needs when two non-overlapping matches share a
    /// start index.
    /// </summary>
    private List<(int RuleOrder, PronunciationMatch Match)> CollectNonOverlappingMatches(string text)
    {
        var accepted = new List<(int RuleOrder, PronunciationMatch Match)>();
        for (var ruleOrder = 0; ruleOrder < rules.Count; ruleOrder++)
        {
            var (pattern, rule) = rules[ruleOrder];
            try
            {
                foreach (System.Text.RegularExpressions.Match occurrence in pattern.Matches(text))
                {
                    var word = occurrence.Groups["word"];
                    var candidate = new PronunciationMatch(word.Index, word.Length, rule);
                    if (accepted.Any(existing => Overlaps(existing.Match, candidate)))
                        continue; // An earlier rule already claimed this span (F97.3) — it wins.

                    accepted.Add((ruleOrder, candidate));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological rule — skip it rather than fault the whole render (F68.5).
            }
        }

        return accepted;
    }

    private static bool Overlaps(PronunciationMatch first, PronunciationMatch second) =>
        first.Index < second.Index + second.Length && second.Index < first.Index + first.Length;

    private static Regex CompilePattern(PronunciationRule rule)
    {
        var wordIndex = rule.Pattern.IndexOf(rule.Word, StringComparison.OrdinalIgnoreCase);
        var prefix = rule.Pattern[..wordIndex];
        var word = rule.Pattern.Substring(wordIndex, rule.Word.Length);
        var suffix = rule.Pattern[(wordIndex + rule.Word.Length)..];

        var leadingBoundary = LiteralRegexPosture.StartsWithWordChar(rule.Pattern) ? @"\b" : string.Empty;
        var trailingBoundary = LiteralRegexPosture.EndsWithWordChar(rule.Pattern) ? @"\b" : string.Empty;
        var pattern =
            $"{leadingBoundary}{Regex.Escape(prefix)}(?<word>{Regex.Escape(word)}){Regex.Escape(suffix)}{trailingBoundary}";

        return LiteralRegexPosture.Compile(pattern);
    }
}
