namespace GenWave.Tts;

using System.Collections.Concurrent;

/// <summary>
/// Process-lifetime, per-rule fired counters for pronunciation rules (SPEC F97.5, STORY-253 AC4) —
/// how many times each (<see cref="PronunciationRule.Pattern"/>, <see cref="PronunciationRule.Word"/>)
/// identity has actually annotated booth-bound text since this process started. Incremented by
/// <see cref="PronunciationRuleHitReporter"/> immediately after a REAL render's markup composition
/// finds a match — never an audition (the admin TTS preview, PLAN T274; see
/// <see cref="PronunciationRuleHitReporter"/>'s own remarks for the exact <c>IsAudition</c>-gated
/// mechanism that now enforces this, replacing the earlier construction-only exclusion) — and read
/// by a future rules API (T144, PLAN.md) so an operator can confirm a saved rule is actually firing
/// on-air. In-memory only, mirroring <see cref="CorrectionsFiredStats"/>' own no-persistence
/// contract — restarting the api resets every count to zero: counts since boot are the honest
/// scope, never a lifetime total.
///
/// <para>
/// Keyed by (<see cref="PronunciationRule.Pattern"/>, <see cref="PronunciationRule.Word"/>) — the
/// SAME identity <see cref="PronunciationRuleSet.Merge"/> already uses to decide which of a station
/// rule and a persona rule sharing that identity is IN EFFECT (persona wins, F97.4): at most one
/// rule can ever occupy a given identity in a single merged snapshot, so this key alone already
/// distinguishes a station-only rule from an unrelated persona-only rule — no separate source tag
/// is needed for that. What this store does NOT track is provenance ACROSS TIME: if an operator's
/// station rule for an identity is later shadowed by a persona card defining the identical identity
/// with a different Ipa, hits recorded before and after that swap land in the same bucket. Accepted,
/// and stated honestly here — the same "counts since boot, not a full audit trail" scope the
/// no-persistence contract above already carries.
/// </para>
/// </summary>
public sealed class PronunciationRuleHitStats
{
    readonly ConcurrentDictionary<string, Hit> fired = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Increments the fired count for the (<paramref name="pattern"/>, <paramref name="word"/>)
    /// identity by one.</summary>
    public void RecordFired(string pattern, string word) =>
        fired.AddOrUpdate(
            Key(pattern, word),
            _ => new Hit(pattern, word, 1),
            (_, existing) => existing with { Fired = existing.Fired + 1 });

    /// <summary>
    /// A snapshot of every rule that has fired at least once since process start, in no particular
    /// order. A rule that has never fired is simply absent — never a zero-count row.
    /// </summary>
    public IReadOnlyList<(string Pattern, string Word, long Fired)> Snapshot() =>
        fired.Values.Select(hit => (hit.Pattern, hit.Word, hit.Fired)).ToList();

    // Mirrors PronunciationRuleSet's own identity-key delimiter (PersonaOverStationMerge's shared
    // constant) so a (pattern, word) pair that could never collide there can't collide here either.
    static string Key(string pattern, string word) =>
        $"{pattern}{PersonaOverStationMerge.IdentityFieldSeparator}{word}";

    sealed record Hit(string Pattern, string Word, long Fired);
}
