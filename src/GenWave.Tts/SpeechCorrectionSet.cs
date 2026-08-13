using System.Text.RegularExpressions;

namespace GenWave.Tts;

/// <summary>
/// Immutable, precompiled collection of operator pronunciation corrections (SPEC F68.5). Each
/// rule's <see cref="SpeechCorrection.From"/> is <see cref="Regex.Escape"/>d before compilation —
/// operator text becomes a literal match, never an arbitrary pattern — with word-boundary anchors
/// added only where the boundary falls on a word-character edge (so a <c>From</c> that starts or
/// ends with a non-word character is not force-anchored there), via the shared
/// <see cref="LiteralRegexPosture"/> helper. Matching is case-insensitive and every compiled rule
/// carries a bounded match timeout so a pathological rule cannot hang the render path; a rule that
/// times out is skipped rather than allowed to fault the whole chokepoint.
///
/// <para>
/// Context conditions (gh-#161): a rule carrying <see cref="SpeechCorrection.WhenPrecededBy"/> /
/// <see cref="SpeechCorrection.WhenFollowedBy"/> compiles those <c>|</c>-separated literal words
/// into zero-width assertions around the same escaped <c>From</c> core — each alternative is
/// <see cref="Regex.Escape"/>d and boundary-anchored exactly like <c>From</c> itself, matched
/// case-insensitively by the same compiled pattern. The neighbouring word may be separated from
/// the match by whitespace/punctuation (<c>wind-down</c>, <c>wind, down</c>) but never across a
/// sentence end (<c>.</c>/<c>!</c>/<c>?</c>): "I need to wind. Down the road…" is not "wind
/// down". Rules still apply strictly in operator order, and a context rule rewrites only its
/// matching occurrences — so "specific rule first, general rule after" composes naturally
/// (<c>wind→wynd when followed by down|up</c>, then an unconditional <c>wind→winnd</c> catches
/// the rest). A condition that parses to no words at all (blank, or only <c>|</c> separators) is
/// treated as absent, so a blank field is a no-op by intent, mirroring the blank-<c>From</c> rule
/// below.
/// </para>
/// </summary>
public sealed class SpeechCorrectionSet
{
    // Gap allowed between a context word and the match: whitespace/punctuation, but never a
    // sentence terminator — "followed by down" must not reach across "wind. Down the road".
    private const string ContextGap = @"[^\w.!?]+";

    private readonly IReadOnlyList<(Regex Pattern, SpeechCorrection Rule)> rules;

    private SpeechCorrectionSet(IReadOnlyList<(Regex Pattern, SpeechCorrection Rule)> rules)
    {
        this.rules = rules;
    }

    /// <summary>An empty correction set — normalization runs with no operator rules applied.</summary>
    public static SpeechCorrectionSet Empty { get; } = new([]);

    /// <summary>
    /// Compiles operator corrections into an immutable, matchable set. A correction whose
    /// <see cref="SpeechCorrection.From"/> is null, empty, or whitespace-only is skipped rather
    /// than compiled: a blank rule is a no-op by intent, never a corruptor — an unguarded empty
    /// pattern matches at every position in the text and would shred it character by character.
    /// A null element in <paramref name="corrections"/> itself — System.Text.Json happily produces
    /// one from a stored array like <c>"[null]"</c>, even though nothing here declares that shape —
    /// is skipped the same way, so a malformed row degrades this rule rather than throwing an NRE
    /// that would escape <see cref="SpeechCorrectionProvider.Build"/>'s degrade-to-Empty contract.
    /// </summary>
    public static SpeechCorrectionSet Create(IEnumerable<SpeechCorrection> corrections)
    {
        ArgumentNullException.ThrowIfNull(corrections);

        var compiled = corrections
            .Where(correction => correction is not null && !string.IsNullOrWhiteSpace(correction.From))
            .Select(correction => (Pattern: CompilePattern(correction), Rule: correction))
            .ToList();

        return new SpeechCorrectionSet(compiled);
    }

    /// <summary>
    /// Merges two correction sets: every <paramref name="card"/> rule is ordered AHEAD of every
    /// <paramref name="station"/> rule (SPEC F97.4, amending the shipped station-over-card
    /// precedence F71.7 established) — the shared invariant <see cref="PersonaOverStationMerge"/>
    /// documents in full, realized here through <see cref="Apply"/>'s own in-order,
    /// sequential-rewrite semantics: rules run in order, and each rewrite works over the text the
    /// rules before it already produced, so a card rule for a sub-phrase
    /// (<c>"MacLeod Duncan"</c>) or any other non-identical overlap still gets first crack at the
    /// text, not only a rule whose <see cref="SpeechCorrection.From"/> plus canonicalized context
    /// conditions is byte-identical (case-insensitive) to a station rule's.
    ///
    /// <para>
    /// That guarantee is scoped to the input text, not to every downstream effect of applying the
    /// rules — <see cref="Apply"/> still runs every station rule strictly AFTER every surviving
    /// card rule, so it does NOT hold that the persona's own output is untouchable, nor that a
    /// card rule can keep benefiting from running after a station rule the way it could pre-flip:
    /// a station rule can still match and rewrite text that a card rule's own replacement
    /// introduced (the persona's output gets clobbered from under it), and a card rule can no
    /// longer post-process a station rule's replacement output, because the card rule already had
    /// its one turn before the station rule ever produced that text.
    /// </para>
    /// A station rule whose identity IS identical to a card rule's is still dropped rather than
    /// appended after it — applying both back-to-back is pure waste once the card rule already
    /// rewrote the text, and leaving it in would let a stale rule reappear in <see cref="Rules"/>.
    /// Identity and ordering do two different jobs: identity dedupes an exact duplicate, ordering
    /// decides every other overlap. Delegates to <see cref="PersonaOverStationMerge.MergeByIdentity{T}"/>,
    /// the seam <see cref="PronunciationRuleSet.Merge"/> shares so the two can't drift apart again.
    /// </summary>
    public static SpeechCorrectionSet Merge(SpeechCorrectionSet station, SpeechCorrectionSet card)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(card);

        return new SpeechCorrectionSet(
            PersonaOverStationMerge.MergeByIdentity(station.rules, card.rules, item => IdentityKey(item.Rule)));
    }

    /// <summary>
    /// The compiled rules, in the exact order <see cref="Apply"/> walks them. Exists solely so
    /// <see cref="SpeechCorrectionProvider"/> and <see cref="ActivePersonaCorrectionsCache"/> can
    /// derive their content-fingerprint cache-key terms (SPEC F68.5, F71.7) from what this set
    /// actually compiled — after the null/blank-From filtering <see cref="Create"/> already
    /// applies — rather than re-deriving or duplicating that filter against the raw operator JSON.
    /// </summary>
    internal IEnumerable<SpeechCorrection> Rules => rules.Select(rule => rule.Rule);

    /// <summary>
    /// Returns this set minus every rule <paramref name="drop"/> selects, reporting the removed
    /// rules (in set order) via <paramref name="removed"/> — the seam
    /// <see cref="RuleOverCorrectionPrecedence.SuppressFor"/> filters through (gh-#491). Compiled
    /// entries are carried over, never recompiled, exactly as <see cref="Merge"/> already
    /// constructs from existing compiled pairs; when nothing is dropped, THIS instance is returned
    /// and no new set is allocated (the common no-collision render).
    /// </summary>
    internal SpeechCorrectionSet Without(
        Func<SpeechCorrection, bool> drop, out IReadOnlyList<SpeechCorrection> removed)
    {
        List<SpeechCorrection>? dropped = null;
        List<(Regex Pattern, SpeechCorrection Rule)>? kept = null;

        for (var i = 0; i < rules.Count; i++)
        {
            if (drop(rules[i].Rule))
            {
                // First drop: materialize the kept-so-far prefix once, lazily — see the summary's
                // no-allocation contract for the no-collision path.
                kept ??= [.. rules.Take(i)];
                (dropped ??= []).Add(rules[i].Rule);
                continue;
            }

            kept?.Add(rules[i]);
        }

        removed = dropped ?? (IReadOnlyList<SpeechCorrection>)[];
        return dropped is null ? this : new SpeechCorrectionSet(kept!);
    }

    /// <summary>
    /// Test-only seam: compiles a single rule from a raw regular expression pattern, bypassing the
    /// <see cref="Regex.Escape"/> step <see cref="Create"/> always applies to operator text. Exists
    /// to exercise the per-rule match timeout deterministically — a pathological pattern cannot be
    /// produced through the escaped, public path. Production code must always go through
    /// <see cref="Create"/>.
    /// </summary>
    internal static SpeechCorrectionSet FromRawPattern(string pattern, string replacement)
    {
        var regex = LiteralRegexPosture.Compile(pattern);
        return new SpeechCorrectionSet([(regex, new SpeechCorrection(pattern, replacement))]);
    }

    /// <summary>
    /// Applies every rule in order, skipping any rule whose match times out. <paramref
    /// name="firedFroms"/> carries every rule's <see cref="SpeechCorrection.From"/> that actually
    /// changed the text, in rule order — empty when nothing fired. This set stays pure (SPEC
    /// F68.7): no logging or counting happens here; <see cref="NormalizingTtsSynthesizer"/> is the
    /// sole reader of <paramref name="firedFroms"/> and does that work itself.
    /// </summary>
    internal string Apply(string text, out IReadOnlyList<string> firedFroms)
    {
        var result = text;
        List<string>? fired = null;

        foreach (var (pattern, rule) in rules)
        {
            try
            {
                var before = result;
                result = pattern.Replace(result, _ => rule.To);
                if (result != before)
                    (fired ??= []).Add(rule.From);
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological rule — skip it rather than fault the whole chokepoint (F68.5).
            }
        }

        firedFroms = fired ?? new List<string>();
        return result;
    }

    private static Regex CompilePattern(SpeechCorrection correction)
    {
        var escaped = Regex.Escape(correction.From);
        var leadingBoundary = LiteralRegexPosture.StartsWithWordChar(correction.From) ? @"\b" : string.Empty;
        var trailingBoundary = LiteralRegexPosture.EndsWithWordChar(correction.From) ? @"\b" : string.Empty;
        var lookbehind = BuildContextAssertion(correction.WhenPrecededBy, isLookbehind: true);
        var lookahead = BuildContextAssertion(correction.WhenFollowedBy, isLookbehind: false);
        var pattern = $"{lookbehind}{leadingBoundary}{escaped}{trailingBoundary}{lookahead}";

        return LiteralRegexPosture.Compile(pattern);
    }

    /// <summary>
    /// Compiles one context condition into a zero-width assertion (gh-#161), or nothing when the
    /// condition is absent/blank. Each <c>|</c>-separated alternative is escaped and
    /// boundary-anchored exactly like <c>From</c> itself; the whole assertion inherits the
    /// pattern-level IgnoreCase, so context matching is case-insensitive by the same rule.
    /// </summary>
    private static string BuildContextAssertion(string? contextWords, bool isLookbehind)
    {
        var alternatives = ParseContextAlternatives(contextWords);
        if (alternatives.Count == 0)
            return string.Empty;

        var alternation = string.Join("|", alternatives.Select(CompileContextWord));
        return isLookbehind
            ? $"(?<=(?:{alternation}){ContextGap})"
            : $"(?={ContextGap}(?:{alternation}))";
    }

    private static string CompileContextWord(string word)
    {
        var leadingBoundary = LiteralRegexPosture.StartsWithWordChar(word) ? @"\b" : string.Empty;
        var trailingBoundary = LiteralRegexPosture.EndsWithWordChar(word) ? @"\b" : string.Empty;
        return $"{leadingBoundary}{Regex.Escape(word)}{trailingBoundary}";
    }

    /// <summary>Splits a raw context condition into its literal word alternatives: <c>|</c>-separated,
    /// trimmed, blanks dropped. Empty result means "no condition".</summary>
    private static IReadOnlyList<string> ParseContextAlternatives(string? contextWords) =>
        string.IsNullOrWhiteSpace(contextWords)
            ? []
            : contextWords.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// A rule's merge identity (see <see cref="Merge"/>): From plus both canonicalized context
    /// conditions, field-separated with <see cref="PersonaOverStationMerge.IdentityFieldSeparator"/>
    /// — the same delimiter <see cref="PronunciationRuleSet"/>'s own identity key and <see
    /// cref="CorrectionsFingerprint"/> share, so all three can't drift onto different delimiters
    /// for the same field-separation problem. Contexts canonicalize through
    /// <see cref="ParseContextAlternatives"/> so <c>" down |up"</c> and <c>"down|up"</c> are the
    /// same condition; a context-free rule folds to <c>From</c> + two empty fields — exactly the
    /// From-only identity pre-gh-#161 merges used.
    /// </summary>
    private static string IdentityKey(SpeechCorrection rule) =>
        $"{rule.From}{PersonaOverStationMerge.IdentityFieldSeparator}{CanonicalContext(rule.WhenPrecededBy)}" +
        $"{PersonaOverStationMerge.IdentityFieldSeparator}{CanonicalContext(rule.WhenFollowedBy)}";

    private static string CanonicalContext(string? contextWords) =>
        string.Join('|', ParseContextAlternatives(contextWords));
}
