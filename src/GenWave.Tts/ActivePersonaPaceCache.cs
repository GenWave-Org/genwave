namespace GenWave.Tts;

using Microsoft.Extensions.Logging;
using GenWave.Core.Abstractions;
using GenWave.Core.Logging;

/// <summary>
/// Bridges the active persona's card speaking pace (<c>VoiceSpec.Pace</c>, SPEC F98.1-F98.3) into
/// the render path with the same bounded-TTL poll <see cref="ActivePersonaCorrectionsCache"/> and
/// <see cref="ActivePersonaPronunciationRulesCache"/> already use for their own card-derived facts
/// — see either class's own remarks for the full "why a TTL, not a subscription" rationale, not
/// restated here.
///
/// Unlike its two siblings, <see cref="Current"/> is not a resolved rule set with its own content
/// fingerprint — a single speaking-rate multiplier already IS the term <see cref="TtsSegmentSource"/>
/// needs for its cache key, the same way it already folds in <c>TtsRenderContext.Voice</c> directly
/// rather than a fingerprint of it. Validation (SPEC F98.2, PLAN T140) happens HERE, at refresh
/// time, via <see cref="TtsPace.Clamp"/> — never at the engine adapter — so every reader of
/// <see cref="Current"/> always sees a value that is already safe to serialize and safe to send to
/// Kokoro.
///
/// <para>
/// <b>The degenerate-value WARN is LATCHED, not per-poll</b> (review finding): <see cref="TtsPace"/>
/// only classifies — logging lives here because ONLY this class knows whether a bad value is a
/// STANDING one. Without a latch, a card that never gets corrected would log the same WARN on
/// every single <see cref="StalenessBound"/> refresh forever. <see cref="lastWarnedDegenerateRawPace"/>
/// mirrors <c>OnAirPersonaAccessor.WarnOnce</c>'s own dedup idiom: keyed on the raw value rather
/// than a persona id (a <see cref="GenWave.Core.Domain.PersonaCard"/> carries no id of its own to
/// key on), cleared the moment a refresh resolves clean so a LATER, genuinely new relapse still
/// gets its own WARN — the same "clear on success" shape
/// <c>OnAirPersonaAccessor.scheduleFaultWarned</c> already uses for its own outage latch.
/// </para>
/// </summary>
public sealed class ActivePersonaPaceCache(
    IActivePersonaAccessor personaAccessor, TimeProvider timeProvider, ILogger<ActivePersonaPaceCache> logger)
{
    /// <summary>Same bound as <see cref="ActivePersonaCorrectionsCache.StalenessBound"/> — all
    /// three card-derived caches read the same card on the same cadence, just project out a
    /// different field.</summary>
    public static readonly TimeSpan StalenessBound = TimeSpan.FromSeconds(30);

    readonly SemaphoreSlim refreshGate = new(1, 1);

    DateTimeOffset lastRefreshedAt = DateTimeOffset.MinValue;

    // WarnOnce latch (review finding) — mutated only inside refreshGate, exactly like
    // lastRefreshedAt above, so a plain field (no Interlocked/volatile) is safe: RefreshIfStaleAsync
    // is this cache's only writer and always holds the gate while touching it.
    double? lastWarnedDegenerateRawPace;

    // A single double wrapped in a record so the field can still be swapped atomically via one
    // volatile reference assignment (mirrors the Snapshot discipline ActivePersonaCorrectionsCache
    // and ActivePersonaPronunciationRulesCache already use) — `volatile` cannot apply to `double`
    // itself (CS0677).
    volatile Snapshot snapshot = new(TtsPace.EngineDefault);

    /// <summary>The most recently cached, already-VALIDATED card pace — see the class remarks for
    /// exactly how stale this is allowed to get. Resolves to <see cref="TtsPace.EngineDefault"/> for
    /// "no active persona" or "an active card whose Pace failed validation" alike — <c>VoiceSpec.Pace</c>'s
    /// own "engine default" sentinel either way.</summary>
    public double Current => snapshot.Pace;

    /// <summary>
    /// Re-reads the active persona's card through <see cref="IActivePersonaAccessor.ResolveCardAsync"/>
    /// and refreshes <see cref="Current"/> when the cache has aged past <see cref="StalenessBound"/>;
    /// a no-op otherwise. Never throws (mirrors the accessor's own never-throws contract): a
    /// no-persona/no-card/no-Voice/store-fault result all resolve to <see cref="TtsPace.EngineDefault"/>
    /// rather than propagating. <c>card.Voice</c> is declared non-nullable but a partial
    /// <c>persona.definition</c> JSONB document can still deserialize it as <see langword="null"/>
    /// (review finding) — <c>card?.Voice is { } voice</c> guards that without trusting the compiler's
    /// nullability annotation over what Postgres can actually hand back.
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
            var rawPace = card?.Voice is { } voice ? voice.Pace : TtsPace.EngineDefault;

            if (TtsPace.IsDegenerate(rawPace))
                WarnOnceForDegenerateValue(rawPace, card?.Name);
            else
                lastWarnedDegenerateRawPace = null; // resolved clean — a later relapse warns again

            snapshot = new Snapshot(TtsPace.Clamp(rawPace));
            lastRefreshedAt = now;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    void WarnOnceForDegenerateValue(double rawPace, string? personaName)
    {
        // double.Equals, NOT ==: NaN == NaN is always false by IEEE 754 (unlike double.Equals,
        // which treats two NaNs as equal) — the primary degenerate value this latch exists for
        // would otherwise never dedupe against itself and re-WARN on every single poll.
        if (lastWarnedDegenerateRawPace is { } lastWarned && lastWarned.Equals(rawPace))
            return; // same standing-bad value as last refresh — already warned, stay quiet

        lastWarnedDegenerateRawPace = rawPace;
        var name = LogSanitize.Strip(personaName);
        logger.LogWarning(
            "Persona {PersonaName} has an invalid Pace ({RawPace}) — rendering at the engine " +
            "default ({EngineDefault}) instead of failing this render (SPEC F98.2)",
            name.Length > 0 ? name : "(unnamed)", rawPace, TtsPace.EngineDefault);
    }

    sealed record Snapshot(double Pace);
}
