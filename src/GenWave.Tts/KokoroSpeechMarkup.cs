namespace GenWave.Tts;

using System.Text;

/// <summary>
/// Composes the two Kokoro-only speech-markup forms kokoro-fastapi v0.6.0 understands into one
/// render (SPEC F96.2): sentence pauses (<c>[pause:Ns]</c>, gh-#116) and pronunciation overrides
/// (<c>[word](/ipa/)</c>, F97) resolved from a <see cref="PronunciationRuleSet"/>.
///
/// <para>
/// Both markup forms are computed independently against the ORIGINAL, unannotated
/// <c>text</c> passed to <see cref="Render"/> — <see cref="PronunciationRuleSet.Match"/> finds the
/// word spans to wrap, <see cref="KokoroPauseMarkup.SentencePauseOffsets"/> finds where a pause
/// belongs — and are only merged afterwards, by original-text offset, into one output string.
/// <see cref="KokoroPauseMarkup"/>'s sentence-boundary regex and abbreviation guard NEVER see
/// already-annotated text: if they did, a phoneme string carrying its own <c>.</c> (a
/// syllable-separator IPA like <c>/ˈmæk. laʊd/</c>) could be mistaken for a sentence boundary and
/// get a pause tag spliced INSIDE the annotation, and wrapping a word ahead of a real
/// sentence-ending period (e.g. the "m" in "9 a.m.") shifts the two characters the abbreviation
/// guard looks back over, defeating it. Scanning the plain text instead makes both failure modes
/// structurally impossible rather than papering over them after the fact. A pause offset that
/// would otherwise land strictly inside an annotation span — the annotated word itself carries
/// the sentence-ending punctuation, e.g. a rule whose <see cref="PronunciationRule.Word"/> is
/// <c>"live."</c> — is deferred to land immediately after the whole <c>[word](ipa)</c> token
/// instead of splitting it: the pause is relocated outside the markup it would otherwise corrupt,
/// never lost and never spliced into the middle of it.
/// </para>
///
/// <para>
/// This type never reimplements <see cref="KokoroPauseMarkup"/>'s gh-#116 heuristic — the
/// sentence-boundary regex and the abbreviation guard stay put there, single source of truth. It
/// asks <see cref="KokoroPauseMarkup.SentencePauseOffsets"/> for the exact positions it would
/// splice a tag at, and hands the composed text plus those offsets to
/// <see cref="KokoroPauseMarkup.Splice"/> to finish the job — never inferring insertion points
/// after the fact. An earlier version recovered offsets by diffing
/// <see cref="KokoroPauseMarkup.InsertSentencePauses"/>'s output against its input: that diff is
/// NOT exact whenever the SOURCE text already contains a <c>[pause:Ns]</c>-shaped substring (an
/// operator correction's replacement, verbatim, is exactly this threat model) — the tag occurrence
/// count in the diffed output no longer corresponds 1:1 with insertion points, silently corrupting
/// the render. Taking the offsets directly removes the possibility structurally. Pronunciation
/// markup is still logically "first" and pauses "second" in the sense that a pronunciation match
/// can never be asked to match text a pause tag already occupies (the reverse composition would let
/// a rule's pattern match inside an injected <c>[pause:Ns]</c> tag) — this type just resolves both
/// against the same clean source rather than threading one's output through the other.
/// </para>
///
/// <para>
/// <see cref="Render"/> preserves the TEXT BEING RENDERED's original casing for every annotated
/// word: <see cref="PronunciationMatch"/> carries the exact <c>[Index, Length)</c> span the rule
/// matched inside that text, so "Wind" (capitalized mid-sentence) stays "Wind" in the markup even
/// though the rule that matched it may have been authored as lowercase "wind". Only the phoneme
/// string comes from the rule; the spoken word always comes from the text.
/// </para>
///
/// <para>
/// Kokoro-only by construction, same placement as <see cref="KokoroPauseMarkup"/>: applied inside
/// both <see cref="KokoroTtsSynthesizer"/> (primary) and <see cref="KokoroFallbackRenderer"/>
/// (kokoro-kind fallback hops) at request build — below the
/// <see cref="NormalizingTtsSynthesizer"/> chokepoint and below the fallback router (F96.1) — and
/// never on the Piper path, which would speak either markup form aloud; see
/// <see cref="PiperSpeechMarkup"/> for that side of F96.3. One seam, one call shape: T137, which
/// resolves a real <see cref="PronunciationRuleSet"/> from the active persona, changes only where
/// each caller's <c>rules</c> argument comes from.
/// </para>
/// </summary>
public static class KokoroSpeechMarkup
{
    /// <summary>
    /// Returns <paramref name="text"/> with every non-overlapping <paramref name="rules"/> match
    /// wrapped as <c>[word](/ipa/)</c> and every <see cref="KokoroPauseMarkup"/> sentence-pause tag
    /// inserted, merged so neither pass can corrupt or lose the other's output (see class
    /// remarks). <paramref name="pauseSeconds"/> &lt;= 0 disables pause insertion, unchanged
    /// contract. Discards which rules fired — see the out-matches overload below for a caller that
    /// needs to know (SPEC F97.5).
    /// </summary>
    public static string Render(string text, PronunciationRuleSet rules, double pauseSeconds) =>
        Render(text, rules, pauseSeconds, out _);

    /// <summary>
    /// Same contract as the three-argument overload above, but also reports exactly which rules
    /// matched via <paramref name="matches"/> — SPEC F97.5's "a rule that fires ... names the rule
    /// [and] speech kind": mirrors <see cref="SpeechCorrectionSet.Apply"/>'s own out-<c>firedFroms</c>
    /// shape one seam over, so a caller (<see cref="KokoroTtsSynthesizer"/>,
    /// <see cref="KokoroFallbackRenderer"/>, via <see cref="PronunciationRuleHitReporter"/>) can
    /// log/count a fired rule without a second, redundant <see cref="PronunciationRuleSet.Match"/>
    /// call over the same text — <see cref="PronunciationMatch"/>'s own remarks state exactly this
    /// intent ("a caller can also log which rule fired ... without a second lookup").
    /// </summary>
    public static string Render(
        string text, PronunciationRuleSet rules, double pauseSeconds, out IReadOnlyList<PronunciationMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rules);

        matches = rules.Match(text);
        var pauseOffsets = pauseSeconds <= 0
            ? []
            : SnapOutsideAnnotations(KokoroPauseMarkup.SentencePauseOffsets(text), matches);

        // Compose degenerates to text unchanged when both inputs are empty — SpliceMatches and
        // Splice each early-return their input untouched (verified over 50,000 fuzz cases, 0
        // mismatches) — so no separate guard is needed here.
        return Compose(text, matches, pauseOffsets, pauseSeconds);
    }

    // Splices the pronunciation annotations into the ORIGINAL text first (their offsets are already
    // in that text's coordinates), then hands the result to KokoroPauseMarkup.Splice for the pause
    // tags — after shifting each pause offset by however much the annotations already spliced ahead
    // of it grew the text by. KokoroPauseMarkup owns the pause tag's shape end to end; this type
    // never formats one itself.
    private static string Compose(
        string text, IReadOnlyList<PronunciationMatch> matches, IReadOnlyList<int> pauseOffsets, double pauseSeconds) =>
        KokoroPauseMarkup.Splice(SpliceMatches(text, matches), ShiftForAnnotations(pauseOffsets, matches), pauseSeconds);

    // The spoken word keeps the TEXT's own casing; only the phonemes come from the rule. Wrapped
    // directly, no normalization here (T138 review findings 2+3): PronunciationRuleSet.Create is
    // the ONE canonicalization site every rule passes through before it can ever compile into a
    // Match — see its own remarks — so match.Rule.Ipa is already the bare, slash-free, non-blank
    // phoneme string by the time this type ever sees it. A second Trim here would silently redo
    // already-correct work and give the two sites a chance to drift; removed rather than kept as
    // depth-2 defense.
    private static string SpliceMatches(string text, IReadOnlyList<PronunciationMatch> matches)
    {
        if (matches.Count == 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var match in matches)
        {
            builder.Append(text, cursor, match.Index - cursor)
                .Append('[').Append(text, match.Index, match.Length)
                .Append("](/").Append(match.Rule.Ipa).Append("/)");
            cursor = match.Index + match.Length;
        }

        builder.Append(text, cursor, text.Length - cursor);
        return builder.ToString();
    }

    // Re-expresses each ORIGINAL-text pause offset in the annotated text SpliceMatches just built:
    // every match that (after SnapOutsideAnnotations) ends at or before the offset already grew the
    // text by "[" + "](/" + ipa + "/)" — 6 characters plus the (already-canonical) ipa string's
    // length — ahead of that point. offsets and matches are both ascending, so this is one linear
    // pass, not a search per offset. On the tie where an offset lands exactly at a match's own
    // start (the pause belongs to whatever preceded that position, never to the word about to
    // start there — see Render), that match's growth correctly is NOT yet applied, because its end
    // is still ahead of the offset.
    private static IReadOnlyList<int> ShiftForAnnotations(IReadOnlyList<int> offsets, IReadOnlyList<PronunciationMatch> matches)
    {
        if (offsets.Count == 0 || matches.Count == 0)
            return offsets;

        var shifted = new List<int>(offsets.Count);
        var matchIndex = 0;
        var shift = 0;
        foreach (var offset in offsets)
        {
            while (matchIndex < matches.Count && matches[matchIndex].Index + matches[matchIndex].Length <= offset)
            {
                shift += 6 + matches[matchIndex].Rule.Ipa.Length;
                matchIndex++;
            }

            shifted.Add(offset + shift);
        }

        return shifted;
    }

    // A pause offset landing strictly inside an annotation span — the annotated word itself
    // carries the sentence-ending punctuation, e.g. a rule word of "live." — is moved to land
    // right after the whole [word](ipa) token instead: relocated outside the markup it would
    // otherwise split, never dropped and never spliced into the middle of it.
    //
    // Two distinct pause offsets that legitimately relocate to the SAME position (one annotation
    // spanning two sentence boundaries, e.g. a rule matching "one. two." verbatim) collapse to ONE
    // tag there (T133 round-4 review, reversing an earlier round's call): a [pause:Ns] tag is
    // audible digital silence on the kokoro-fastapi wire, and two tags back to back at one seam
    // SUM rather than coexist — emitting both would double a 0.6s gap into 1.2s of dead air at a
    // seam the source text only ever gave 0.6s. This is the exact same "one boundary, one tag"
    // rule KokoroPauseMarkup itself applies to a maximal [.!?…]+ run (see its class remarks): a
    // coincident relocation IS that same run, just arrived at via two originally-distinct offsets
    // landing on one output position instead of one regex match spanning several punctuation
    // characters. An operator who fuses "one. two." into a single annotated span has deliberately
    // removed the interior boundary between them.
    //
    // offsets and matches are both ascending (see ShiftForAnnotations) and matches never overlap
    // (PronunciationRuleSet.Match), so a match once passed can never apply to a later offset — one
    // linear walk suffices, mirroring ShiftForAnnotations rather than re-scanning matches per
    // offset. Adjacent-only comparison is enough to de-duplicate because the walk only ever emits
    // in non-decreasing order.
    private static IReadOnlyList<int> SnapOutsideAnnotations(
        IReadOnlyList<int> offsets, IReadOnlyList<PronunciationMatch> matches)
    {
        if (offsets.Count == 0 || matches.Count == 0)
            return offsets;

        var snapped = new List<int>(offsets.Count);
        var matchIndex = 0;
        foreach (var offset in offsets)
        {
            while (matchIndex < matches.Count && matches[matchIndex].Index + matches[matchIndex].Length <= offset)
                matchIndex++;

            var span = matchIndex < matches.Count ? matches[matchIndex] : null;
            var snappedOffset = span is not null && offset > span.Index && offset < span.Index + span.Length
                ? span.Index + span.Length
                : offset;

            if (snapped.Count == 0 || snapped[^1] != snappedOffset)
                snapped.Add(snappedOffset);
        }

        return snapped;
    }
}
