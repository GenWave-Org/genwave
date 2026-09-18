using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// Everything the plan phase needs to build one break's <see cref="BreakPlan"/> (SPEC F187.7) — no
/// renderer, no buffer, no engine: a test can build a <see cref="BreakPlan"/> from this with fakes
/// only.
/// </summary>
/// <param name="Previous">The track that just finished, or <see langword="null"/> before the first unit.</param>
/// <param name="Next">The upcoming track, or <see langword="null"/> for a ceremony-only unit (SPEC F186.2e).</param>
/// <param name="UnitDjName">The persona name on air for this unit, or <see langword="null"/> when none is active.</param>
/// <param name="Cadence">The station's cadence toggles (lead-in, back-announce, station-id-every-N).</param>
/// <param name="Identity">The station's identity (name, voice, id).</param>
/// <param name="Now">The instant the plan is built at.</param>
/// <param name="DrainAsOf">
/// The instant deferrals drain as-of, when a straddle reconciliation clamps it (SPEC F186.2a);
/// <see langword="null"/> to drain as-of <see cref="Now"/>.
/// </param>
/// <param name="Hold">Deferral kinds held past this break's drain (SPEC F186.2a's held sign-on).</param>
/// <param name="QueuedAhead">How much audio is already queued ahead, used to clamp a held deferral's earliest re-arm.</param>
public sealed record BreakContext(
    MediaItem? Previous,
    MediaItem? Next,
    string? UnitDjName,
    CadenceConfig Cadence,
    StationIdentity Identity,
    DateTimeOffset Now,
    DateTimeOffset? DrainAsOf,
    IReadOnlySet<SpeechDeferralKind> Hold,
    TimeSpan QueuedAhead);
