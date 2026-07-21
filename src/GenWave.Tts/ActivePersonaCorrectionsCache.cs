namespace GenWave.Tts;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

/// <summary>
/// Bridges the active persona's card corrections (SPEC F71.7, STORY-193) into the render path with
/// a bounded staleness window rather than a DB round trip on every render. <see cref="Current"/> is
/// a plain synchronous read (mirrors <see cref="SpeechCorrectionProvider.Current"/>'s own shape) so
/// <see cref="NormalizingTtsSynthesizer.Preview"/> — a synchronous method, SPEC F68.6 — can read it
/// with no await; <see cref="RefreshIfStaleAsync"/> is the async half that keeps it warm, called
/// only from the async render path.
///
/// <para>
/// <b>Why a TTL, not an OnChange subscription:</b> <c>Station:Persona:ActiveId</c> lives in the
/// Host's <c>IOptionsMonitor&lt;StationOptions&gt;</c>, invisible from this project by design — that
/// is exactly why <see cref="IActivePersonaAccessor"/> exists as the seam
/// (<c>GenWave.Core.Abstractions</c>) rather than this project depending on Host option types
/// directly. With no visibility into the pointer's own change token, a bounded poll is the honest,
/// simple mechanism available at this layer (STORY-193's own "keep it simple" instruction) — not a
/// stand-in for a subscription this class could have taken instead.
/// </para>
///
/// <para>
/// <b>Staleness bounds (document, don't hide):</b> a real render (<see cref="RefreshIfStaleAsync"/>
/// called from <see cref="NormalizingTtsSynthesizer.SynthesizeAsync"/>) sees card corrections at
/// most <see cref="StalenessBound"/> old — an activate/deactivate of the active persona, or an edit
/// to its card, reaches the very next render only after that window elapses, never instantly. The
/// synchronous preview path never refreshes at all: it reads whatever <see cref="Current"/> the
/// last real render (if any) populated, up to <see cref="StalenessBound"/> stale, or empty if no
/// render has happened yet since process start — an accepted trade-off for a preview, which is
/// advisory only and never itself a broadcast.
/// </para>
/// </summary>
public sealed class ActivePersonaCorrectionsCache(IActivePersonaAccessor personaAccessor, TimeProvider timeProvider)
{
    /// <summary>How stale <see cref="Current"/> is allowed to get before <see cref="RefreshIfStaleAsync"/>
    /// re-reads the active persona's card. 30s: frequent enough that an operator's activate/edit
    /// reaches on-air within one broadcast segment or so, infrequent enough that it costs nothing
    /// close to per-render DB traffic.</summary>
    public static readonly TimeSpan StalenessBound = TimeSpan.FromSeconds(30);

    // Sentinel for "no active persona, or an active card with no corrections" — a stable literal
    // rather than a hash of empty input, distinct from SpeechCorrectionProvider's own
    // "no-corrections" sentinel (the station side of the F71.7 merge) so the two independent "no
    // rules" cases can never collide with each other either. A station-only deployment (no active
    // persona at all) always folds to this one constant, so its TtsSegmentSource cache key never
    // drifts across renders or restarts.
    const string EmptyContentHash = "no-card-corrections";

    readonly SemaphoreSlim refreshGate = new(1, 1);

    DateTimeOffset lastRefreshedAt = DateTimeOffset.MinValue;

    // Corrections and ContentHash are always derived from the SAME refresh and swapped together via
    // one reference assignment — never two independent volatile fields (mirrors
    // SpeechCorrectionProvider's own Snapshot discipline) — so a reader can never observe Current
    // from one refresh paired with ContentHash from another.
    volatile Snapshot snapshot = new([], EmptyContentHash);

    /// <summary>The most recently cached card corrections — see the class remarks for exactly how stale
    /// this is allowed to be depending on which path is reading it.</summary>
    public IReadOnlyList<SpeechCorrection> Current => snapshot.Corrections;

    /// <summary>
    /// Deterministic content fingerprint of the CURRENT card-corrections snapshot (SPEC F71.7), via
    /// <see cref="CorrectionsFingerprint.Compute"/> over the canonical, ordered (From, To) pairs
    /// actually compiled from <see cref="Current"/> (after the null/blank-From filtering <see
    /// cref="SpeechCorrectionSet.Create"/> applies) — the same canonicalization idiom as <see
    /// cref="SpeechCorrectionProvider.ContentHash"/>, with its own stable empty sentinel (see <see
    /// cref="EmptyContentHash"/>) rather than the station side's. This is the other of the two terms
    /// <see cref="TtsSegmentSource"/> folds into its cache key: same card rules → same key, a card
    /// edit or a persona switch → a new key on the render that next observes it. Inherits <see
    /// cref="RefreshIfStaleAsync"/>'s own staleness bound — this fingerprint can lag a card
    /// change/persona switch by up to <see cref="StalenessBound"/>, exactly as long as <see
    /// cref="Current"/> itself can.
    /// </summary>
    public string ContentHash => snapshot.ContentHash;

    /// <summary>
    /// Re-reads the active persona's card through <see cref="IActivePersonaAccessor.ResolveCardAsync"/>
    /// and refreshes <see cref="Current"/>/<see cref="ContentHash"/> together when the cache has aged
    /// past <see cref="StalenessBound"/>; a no-op otherwise. Never throws (mirrors the accessor's own
    /// never-throws contract): a no-persona/no-card/store-fault result all resolve to an empty
    /// correction list (and the stable <see cref="EmptyContentHash"/> sentinel) rather than
    /// propagating.
    /// </summary>
    public async Task RefreshIfStaleAsync(CancellationToken ct)
    {
        // Every caller (this cache is a DI singleton shared by every concurrent render) takes the
        // gate and re-checks staleness INSIDE it — no unsynchronized fast-path read of
        // lastRefreshedAt outside the lock (DateTimeOffset can't be volatile, so that read would be
        // a genuine data race, not just a relaxed one). An uncontended SemaphoreSlim.WaitAsync costs
        // nothing next to a render's own synthesis latency, so there is no perf reason to avoid it.
        await refreshGate.WaitAsync(ct);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (now - lastRefreshedAt < StalenessBound)
                return;

            var card = await personaAccessor.ResolveCardAsync(ct);
            IReadOnlyList<SpeechCorrection> corrections = card is { Corrections.Count: > 0 }
                ? card.Corrections.Select(correction => new SpeechCorrection(correction.From, correction.To)).ToList()
                : [];
            snapshot = new Snapshot(corrections, ComputeContentHash(corrections));
            lastRefreshedAt = now;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    static string ComputeContentHash(IReadOnlyList<SpeechCorrection> corrections) =>
        CorrectionsFingerprint.Compute(SpeechCorrectionSet.Create(corrections).RulePairs, EmptyContentHash);

    sealed record Snapshot(IReadOnlyList<SpeechCorrection> Corrections, string ContentHash);
}
