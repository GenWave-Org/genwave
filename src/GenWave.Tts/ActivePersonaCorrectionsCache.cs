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

    readonly SemaphoreSlim refreshGate = new(1, 1);

    DateTimeOffset lastRefreshedAt = DateTimeOffset.MinValue;
    volatile IReadOnlyList<SpeechCorrection> current = [];

    /// <summary>The most recently cached card corrections — see the class remarks for exactly how stale
    /// this is allowed to be depending on which path is reading it.</summary>
    public IReadOnlyList<SpeechCorrection> Current => current;

    /// <summary>
    /// Re-reads the active persona's card through <see cref="IActivePersonaAccessor.ResolveCardAsync"/>
    /// and refreshes <see cref="Current"/> when the cache has aged past <see cref="StalenessBound"/>;
    /// a no-op otherwise. Never throws (mirrors the accessor's own never-throws contract): a
    /// no-persona/no-card/store-fault result all resolve to an empty correction list rather than
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
            current = card is { Corrections.Count: > 0 }
                ? card.Corrections.Select(correction => new SpeechCorrection(correction.From, correction.To)).ToList()
                : [];
            lastRefreshedAt = now;
        }
        finally
        {
            refreshGate.Release();
        }
    }
}
