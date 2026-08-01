namespace GenWave.Tts;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Shared canonicalization for a pronunciation-rule content fingerprint (SPEC F97.3), the rule-set
/// sibling of <see cref="CorrectionsFingerprint"/> — same delimiter, same short-digest shape, same
/// "stable sentinel for no rules" idiom, kept as its own type rather than a generic overload because
/// <see cref="PronunciationRule"/>'s <c>{Pattern, Word, Ipa}</c> shape has nothing in common with
/// <see cref="SpeechCorrection"/>'s <c>{From, To}</c> plus context conditions beyond "it's a rule".
/// Both <see cref="PronunciationRuleProvider"/> (station rules) and
/// <see cref="ActivePersonaPronunciationRulesCache"/> (the active persona card's rules) fold their
/// own, independent rule set into a <see cref="TtsSegmentSource"/> cache-key term through this one
/// encoding — same rules always fold to the same fingerprint, changed rules always fold to a new one.
/// </summary>
static class PronunciationRuleFingerprint
{
    // Shares its VALUE with PersonaOverStationMerge.IdentityFieldSeparator/CorrectionsFingerprint's
    // own separator (same control character, same field-separation problem) but is pinned
    // independently, mirroring CorrectionsFingerprint's own remarks on why: this constant feeds a
    // persisted, fleet-visible cache-key term that must stay stable across a deploy.
    const char FieldSeparator = '\x1F';
    const char PairSeparator = '\x1E';

    /// <summary>
    /// Deterministic short SHA-256 hex digest over the canonical, ordered <paramref name="rules"/>,
    /// or <paramref name="emptySentinel"/> when there are none — a stable literal rather than a hash
    /// of empty input, so the no-rules case never depends on the hash algorithm's own behavior.
    /// Callers pass their own <paramref name="emptySentinel"/> so the station and card "no rules"
    /// cases can never collide with each other.
    /// </summary>
    public static string Compute(IEnumerable<PronunciationRule> rules, string emptySentinel)
    {
        var list = rules.ToList();
        if (list.Count == 0)
            return emptySentinel;

        var canonical = string.Join(PairSeparator, list.Select(CanonicalRule));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest)[..16];
    }

    static string CanonicalRule(PronunciationRule rule) =>
        $"{rule.Pattern}{FieldSeparator}{rule.Word}{FieldSeparator}{rule.Ipa}";
}
