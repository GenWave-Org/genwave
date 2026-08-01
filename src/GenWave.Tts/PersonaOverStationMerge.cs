namespace GenWave.Tts;

/// <summary>
/// The persona-over-station merge mechanism <see cref="SpeechCorrectionSet"/> and
/// <see cref="PronunciationRuleSet"/> both promise to share (SPEC F97.3, F97.4): every card rule
/// is ordered AHEAD of every station rule, so each type's own in-order-application semantics —
/// <see cref="SpeechCorrectionSet.Apply"/>'s sequential rewrite, <see
/// cref="PronunciationRuleSet.Match"/>'s first-rule-claims-the-span overlap policy — resolve
/// precedence by POSITION rather than by a bespoke identity check.
///
/// <para>
/// <b>THE INVARIANT this merge actually provides</b> (fuzz-verified across 20,000+ contended
/// texts, T136 review): no station rule ever pre-empts a card rule. Every card rule gets its turn
/// on the text before any station rule runs — a card rule for a sub-phrase
/// (<c>"MacLeod Duncan"</c>) or any other non-identical overlap still gets first crack at the
/// text, not only a rule whose identity happens to be byte-identical to a station rule's (SPEC
/// F97.4, amending the shipped station-over-card precedence F71.7 established). A card rule CAN
/// still lose an overlap — but only to ANOTHER card rule that precedes it in the card list, never
/// to a station rule. "The persona wins every overlap, full stop" is NOT what this merge
/// guarantees, and must not be restated that way: two card rules can still contend with each
/// other for the same span, and a station rule can still end up claiming a span that no card rule
/// ends up accepting — because the competing card candidate lost to an EARLIER CARD rule, never
/// because the station rule pre-empted it. Each type's own <c>Merge</c> doc comment (<see
/// cref="SpeechCorrectionSet.Merge"/>, <see cref="PronunciationRuleSet.Merge"/>) spells out how
/// ITS OWN in-order-application mechanism realizes this invariant; neither restates the invariant
/// itself, so there is exactly one place to correct it the next time this policy changes.
/// </para>
///
/// <para>
/// A station rule whose identity IS identical (case-insensitive) to a card rule's is still
/// dropped rather than appended after it: running both back-to-back is pure waste once the card
/// rule already ran, and leaving it in would let a stale rule reappear in a caller's <c>Rules</c>
/// listing or be matched twice. Identity and ordering do two different jobs — identity dedupes an
/// exact duplicate, ordering decides every other overlap.
/// </para>
///
/// <para>
/// Mirrors <see cref="LiteralRegexPosture"/> one seam over: that helper keeps the two types'
/// compilation posture from drifting apart; this one keeps their merge precedence — and its prose
/// — from drifting apart the same way. A lesson learned twice: the precedence itself already
/// flipped once (F71.7 → F97.4), and an unqualified "persona wins everywhere" restatement of it
/// already had to be walked back once (T136 review) after living, disagreeing, in three separate
/// files at once.
/// </para>
/// </summary>
internal static class PersonaOverStationMerge
{
    /// <summary>
    /// Delimits an identity key's fields (e.g. From/Pattern plus canonicalized context) with a
    /// control character no operator/persona-authored text plausibly contains — shared so <see
    /// cref="SpeechCorrectionSet"/>, <see cref="PronunciationRuleSet"/>, and <see
    /// cref="CorrectionsFingerprint"/> can't drift onto different delimiters for the same
    /// field-separation problem. Changing this value changes every rule's identity key, which
    /// feeds <see cref="CorrectionsFingerprint"/> — treat it as a breaking change: it re-keys
    /// (and so invalidates) every cached TTS clip across the fleet on the next deploy.
    /// </summary>
    public const char IdentityFieldSeparator = '\x1F';

    /// <summary>
    /// Merges two lists: every <paramref name="card"/> item is ordered ahead of every
    /// <paramref name="station"/> item, and a <paramref name="station"/> item sharing a
    /// <paramref name="card"/> item's <paramref name="identity"/> (case-insensitive) is dropped
    /// rather than appended after it. Generic over the whole item — not constrained to a
    /// <c>(Regex, TRule)</c> shape — because the merge only ever reorders and de-duplicates by
    /// identity, never touches a pattern; a caller whose item is a <c>(Regex, TRule)</c> tuple
    /// passes an <paramref name="identity"/> that projects out the <c>TRule</c> half itself.
    /// </summary>
    public static List<TItem> MergeByIdentity<TItem>(
        IReadOnlyList<TItem> station,
        IReadOnlyList<TItem> card,
        Func<TItem, string> identity)
    {
        var cardKeys = new HashSet<string>(card.Select(identity), StringComparer.OrdinalIgnoreCase);
        var merged = new List<TItem>(card);
        merged.AddRange(station.Where(item => !cardKeys.Contains(identity(item))));

        return merged;
    }
}
