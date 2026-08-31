namespace GenWave.Core.Domain;

/// <summary>
/// The result of <see cref="Abstractions.IMediaCatalog.GetRotationCandidateAsync"/> (SPEC F41.1) — a
/// selected track plus which preference tiers were relaxed to produce it. Either flag being
/// <c>true</c> is diagnostic (F41.5: the Orchestrator logs a WARN naming the relaxed constraint), not
/// an error — the never-drains contract (F41.2/F41.4) means a relaxed pick still beats null.
///
/// <para>
/// <see cref="Energy"/> (SPEC F80.1, F81.5; STORY-213, PLAN T64) is the LUFS-percentile energy
/// <c>GenWave.Orchestration.RankerPersonaPickProvider</c> carries through from its own
/// <c>EnvelopeCandidateRow</c> mapping — <see langword="null"/> for every candidate that query never
/// touched (the plain <see cref="Abstractions.IMediaCatalog.GetRotationCandidateAsync"/>/
/// <see cref="Abstractions.IMediaCatalog.GetEnvelopeCandidateAsync"/> paths never populated it, T62
/// review note). <c>Orchestrator</c>'s trust-but-verify re-check (SPEC F81.5) uses it for a rung-0
/// energy leg alongside the existing genre leg — an unpopulated (<see langword="null"/>) value always
/// passes that leg, the same "unknown never silences" convention <see cref="Abstractions.IMediaCatalog.GetEnvelopeCandidateAsync"/>'s
/// own energy-band predicate honors.
/// </para>
///
/// <para>
/// <see cref="PersonaPick"/> (SPEC F82.6, F83.1) is non-null only for a rung-0 persona pick that won
/// (SPEC F81.6) — the debug-line/T65 taste-context carrier; every envelope-only ladder pick,
/// including the common persona-off case, leaves it <see langword="null"/>.
/// </para>
///
/// <para>
/// <see cref="RequestFulfilled"/> (SPEC F87.6/F87.7, STORY-227, PLAN T90) is <see langword="true"/>
/// only for a candidate <c>GenWave.Orchestration.RequestFulfillmentProvider</c> resolved from a live
/// pending listener request — the fulfillment rung that short-circuits ahead of
/// <see cref="PersonaPick"/>'s own rung, so the two are mutually exclusive in practice (a fulfilled
/// pick never also carries persona diagnostics). Carried through onto <see cref="MediaItem.RequestFulfilled"/>
/// the same way <see cref="PersonaPick"/> itself rides onto <see cref="MediaItem.PersonaPick"/> — the
/// marker vehicle a future copywriter consumer (T91) reads at prompt time. Deliberately NOT part of
/// the persisted F86.1 booth-log pick jsonb (<c>BoothLogPickStamp</c>'s shape is pinned) — this field
/// only ever travels the in-memory Orchestrator → TTS prompt-assembly path.
/// </para>
///
/// <para>
/// <see cref="RotationRelax"/> (SPEC F152.4, STORY-372, PLAN T361) is the R0–R3 step
/// <c>GenWave.Orchestration.MusicSelectionPolicy</c>'s rotation relax ladder landed on to produce
/// THIS candidate — <see langword="null"/> when <c>GenWave.Abstractions.Playout.SegmentEnvelope.Rotation</c>
/// was never set for the pick at all (no ladder ran), never <c>0</c> for that case; <c>0</c> means the
/// predicate was in force and satisfied without relaxing. Set on the winning candidate regardless of
/// whether it came from the rung-0 persona pick (alongside <see cref="PersonaPick"/>) or the terminal
/// SPEC F81.6 fallback (<see cref="PersonaPick"/> null) — the relax step is a property of the PICK, not
/// of which rung supplied it. Carried through onto <see cref="MediaItem.RotationRelax"/> the same way
/// <see cref="PersonaPick"/>/<see cref="RequestFulfilled"/> already are.
/// </para>
/// </summary>
public sealed record RotationCandidate(
    MediaReference Media,
    bool RepeatedRecent,
    bool RepeatedArtist,
    double? Energy = null,
    PersonaPickDiagnostics? PersonaPick = null,
    bool RequestFulfilled = false)
{
    /// <summary>
    /// MED-4 (PLAN T361 review) — a defaulted BODY property, not an 8th positional constructor
    /// parameter: <see cref="RotationCandidate"/> already shipped in the 5.4.x Abstractions NuGet
    /// with a fixed 6-arg ctor/Deconstruct; a 7th positional parameter would silently remove both
    /// from the published binary surface (IL-confirmed), breaking every already-compiled caller
    /// regardless of the new parameter's own default value — the exact <see cref="MediaItem.CrosstalkScript"/>/
    /// <c>GenWave.Core.Events.TrackAired.CrosstalkScript</c> discipline, applied here one cycle late.
    /// Every construction site that needs to set this uses a <c>with</c> expression, never a
    /// positional/named constructor argument.
    /// </summary>
    public int? RotationRelax { get; init; }
}
