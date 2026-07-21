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
    // ASCII Unit Separator / Record Separator (hex escapes below) — delimits a pair's two fields,
    // and each pair from the next, with control characters no operator-authored From/To text will
    // plausibly contain. Two distinct rule sets can then never fold to the same canonical string
    // through field-boundary ambiguity (e.g. From="A", To="BC" vs From="AB", To="C" would otherwise
    // both canonicalize to the same "ABC" with a plain concatenation).
    const char FieldSeparator = '\x1F';
    const char PairSeparator = '\x1E';

    /// <summary>
    /// Deterministic short SHA-256 hex digest over the canonical, ordered (From, To) <paramref
    /// name="pairs"/>, or <paramref name="emptySentinel"/> when there are none — a stable literal
    /// rather than a hash of empty input, so the no-rules case never depends on the hash
    /// algorithm's own behavior and reads unambiguously in logs/cache-file names. Callers pass their
    /// own <paramref name="emptySentinel"/> so two independent "no rules" cases (station vs. card)
    /// can never collide with each other.
    /// </summary>
    public static string Compute(IEnumerable<(string From, string To)> pairs, string emptySentinel)
    {
        var list = pairs.ToList();
        if (list.Count == 0)
            return emptySentinel;

        var canonical = string.Join(PairSeparator, list.Select(pair => $"{pair.From}{FieldSeparator}{pair.To}"));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest)[..16];
    }
}
