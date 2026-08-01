namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using ContextPronunciationRule = GenWave.Core.Domain.PronunciationRule;

/// <summary>
/// Bridges the active persona's card pronunciation rules (SPEC F97.3, F97.4; ARCHITECTURE.md "Make
/// the DJs sound human") into the render path with a bounded staleness window rather than a DB round
/// trip on every render — the pronunciation-rule sibling of <see cref="ActivePersonaCorrectionsCache"/>,
/// mirroring its shape exactly (same TTL mechanism, same never-throws contract, same reasons for a
/// poll instead of a subscription; see that class's own remarks for the full rationale, not restated
/// here). <see cref="Current"/> is a plain synchronous read so a caller resolving alongside
/// <see cref="ActivePersonaCorrectionsCache.Current"/> pays no extra await; <see cref="RefreshIfStaleAsync"/>
/// is the async half that keeps it warm, called only from the async render path
/// (<see cref="TtsSegmentSource"/> — never from inside an engine adapter, SPEC F97.6).
/// </summary>
public sealed class ActivePersonaPronunciationRulesCache(IActivePersonaAccessor personaAccessor, TimeProvider timeProvider)
{
    /// <summary>Same bound as <see cref="ActivePersonaCorrectionsCache.StalenessBound"/> — the two
    /// caches read the same card on the same cadence, just project out a different field.</summary>
    public static readonly TimeSpan StalenessBound = TimeSpan.FromSeconds(30);

    // Sentinel for "no active persona, or an active card with no pronunciation rules" — distinct
    // from PronunciationRuleProvider's own "no-pronunciations" sentinel (the station side) so the
    // two independent "no rules" cases can never collide with each other either.
    const string EmptyContentHash = "no-card-pronunciations";

    readonly SemaphoreSlim refreshGate = new(1, 1);

    DateTimeOffset lastRefreshedAt = DateTimeOffset.MinValue;

    // Rules and ContentHash are always derived from the SAME refresh and swapped together via one
    // reference assignment — never two independent volatile fields — mirrors
    // ActivePersonaCorrectionsCache's own Snapshot discipline.
    volatile Snapshot snapshot = new([], EmptyContentHash);

    /// <summary>The most recently cached card pronunciation rules — see the class remarks for exactly
    /// how stale this is allowed to be.</summary>
    public IReadOnlyList<PronunciationRule> Current => snapshot.Rules;

    /// <summary>
    /// Deterministic content fingerprint of the CURRENT card-rules snapshot (SPEC F97.3), via
    /// <see cref="PronunciationRuleFingerprint.Compute"/> over the canonical, ordered rules actually
    /// compiled from <see cref="Current"/> (after <see cref="PronunciationRuleSet.Create"/>'s own
    /// filtering) — the other of the two terms <see cref="TtsSegmentSource"/> folds into its cache
    /// key (the other is <see cref="PronunciationRuleProvider.ContentHash"/>, the station side).
    /// Inherits <see cref="RefreshIfStaleAsync"/>'s own staleness bound.
    /// </summary>
    public string ContentHash => snapshot.ContentHash;

    /// <summary>
    /// Re-reads the active persona's card through <see cref="IActivePersonaAccessor.ResolveCardAsync"/>
    /// and refreshes <see cref="Current"/>/<see cref="ContentHash"/> together when the cache has aged
    /// past <see cref="StalenessBound"/>; a no-op otherwise. Never throws (mirrors the accessor's own
    /// never-throws contract): a no-persona/no-card/store-fault result all resolve to an empty rule
    /// list (and the stable <see cref="EmptyContentHash"/> sentinel) rather than propagating.
    /// </summary>
    public async Task RefreshIfStaleAsync(CancellationToken ct)
    {
        await refreshGate.WaitAsync(ct);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (now - lastRefreshedAt < StalenessBound)
                return;

            var card = await personaAccessor.ResolveCardAsync(ct);
            IReadOnlyList<PronunciationRule> rules = card is { Pronunciations.Count: > 0 }
                ? card.Pronunciations.Select(ToTtsRule).ToList()
                : [];
            snapshot = new Snapshot(rules, ComputeContentHash(rules));
            lastRefreshedAt = now;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    static PronunciationRule ToTtsRule(ContextPronunciationRule rule) =>
        new(rule.Pattern, rule.Word, rule.Ipa);

    static string ComputeContentHash(IReadOnlyList<PronunciationRule> rules) =>
        PronunciationRuleFingerprint.Compute(PronunciationRuleSet.Create(rules).Rules, EmptyContentHash);

    sealed record Snapshot(IReadOnlyList<PronunciationRule> Rules, string ContentHash);
}
