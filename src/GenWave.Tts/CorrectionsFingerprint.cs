namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Shared canonicalization for a correction-rule content fingerprint (SPEC F68.5, F71.7). Both
/// <see cref="SpeechCorrectionProvider"/> (station rules) and <see cref="ActivePersonaCorrectionsCache"/>
/// (the active persona card's rules) fold their own, independent rule set into a
/// <see cref="TtsSegmentSource"/> cache-key term through this ONE encoding rather than two
/// hand-rolled copies — same rules always fold to the same fingerprint, in this process or the
/// next one, and changed rules always fold to a new one.
/// </summary>
static class CorrectionsFingerprint
{
    // ASCII Unit Separator / Record Separator — delimits a pair's two fields, and each pair from
    // the next, with control characters no operator-authored From/To text will plausibly contain.
    // Two distinct rule sets can then never fold to the same canonical string through
    // field-boundary ambiguity (e.g. From="A", To="BC" vs From="AB", To="C" would otherwise both
    // canonicalize to the same "ABC" with a plain concatenation). FieldSeparator happens to share
    // its VALUE with PersonaOverStationMerge.IdentityFieldSeparator (same control character, same
    // field-separation problem) but is pinned HERE independently rather than derived from that
    // type: this constant feeds a persisted, fleet-visible cache-key term (SPEC F68.5) that must
    // stay stable across a deploy, while PersonaOverStationMerge.IdentityFieldSeparator feeds an
    // in-memory merge-policy identity key (SPEC F97.4) that is free to change with the merge
    // algorithm. Coupling this constant to that type would mean an in-memory-only concern could
    // silently re-key every cached TTS clip across the fleet on a future edit neither author
    // intended to be cache-affecting.
    const char FieldSeparator = '\x1F';
    const char PairSeparator = '\x1E';

    /// <summary>
    /// Deterministic short SHA-256 hex digest over the canonical, ordered <paramref name="rules"/>,
    /// or <paramref name="emptySentinel"/> when there are none — a stable literal rather than a
    /// hash of empty input, so the no-rules case never depends on the hash algorithm's own behavior
    /// and reads unambiguously in logs/cache-file names. Callers pass their own <paramref
    /// name="emptySentinel"/> so two independent "no rules" cases (station vs. card) can never
    /// collide with each other.
    /// </summary>
    public static string Compute(IEnumerable<SpeechCorrection> rules, string emptySentinel)
    {
        var list = rules.ToList();
        if (list.Count == 0)
            return emptySentinel;

        var canonical = string.Join(PairSeparator, list.Select(CanonicalRule));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest)[..16];
    }

    /// <summary>
    /// One rule's canonical term. A context-free rule keeps the original two-field
    /// <c>From␟To</c> encoding byte-for-byte, so every pre-gh-#161 rule set folds to the SAME
    /// fingerprint after this upgrade — no TtsSegmentSource cache invalidation for operators who
    /// never touch the new fields. A rule carrying either context condition appends both context
    /// fields (blank-for-absent), so editing only a rule's context still moves the fingerprint —
    /// and with it the render cache key — exactly like editing its To.
    /// </summary>
    static string CanonicalRule(SpeechCorrection rule)
    {
        var core = $"{rule.From}{FieldSeparator}{rule.To}";
        var hasContext = !string.IsNullOrWhiteSpace(rule.WhenPrecededBy)
            || !string.IsNullOrWhiteSpace(rule.WhenFollowedBy);

        return hasContext
            ? $"{core}{FieldSeparator}{rule.WhenPrecededBy}{FieldSeparator}{rule.WhenFollowedBy}"
            : core;
    }
}
