using System.Text.RegularExpressions;

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
    /// <see cref="SpeechCorrectionSet.Create"/>'s blank-<c>From</c> posture. When
    /// <see cref="PronunciationRule.Word"/> occurs more than once inside
    /// <see cref="PronunciationRule.Pattern"/>, the FIRST occurrence binds (ordinary
    /// <see cref="string.IndexOf(string, StringComparison)"/> semantics) and later occurrences are
    /// unreachable — an operator authoring <c>("the wind wind", "wind", ...)</c> gets the first
    /// "wind" annotated, never the second. A malformed rule is dropped silently from
    /// <see cref="Match"/>, but not invisibly: it never appears in <see cref="Rules"/>, so a caller
    /// can tell a rule never compiled (F97.5) without this set doing any logging of its own
    /// (F68.6). A null element in <paramref name="rules"/> itself — the same JSON-array degenerate
    /// case <see cref="SpeechCorrectionSet.Create"/> documents — is skipped the same way.
    /// </summary>
    public static PronunciationRuleSet Create(IEnumerable<PronunciationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var compiled = rules
            .Where(rule => rule is not null)
            .Select(rule => PronunciationRule.Parse(rule.Pattern, rule.Word, rule.Ipa))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern)
                && !string.IsNullOrWhiteSpace(rule.Word)
                && rule.Pattern.Contains(rule.Word, StringComparison.OrdinalIgnoreCase))
            .Select(rule => (Pattern: CompilePattern(rule), Rule: rule))
            .ToList();

        return new PronunciationRuleSet(compiled);
    }

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
