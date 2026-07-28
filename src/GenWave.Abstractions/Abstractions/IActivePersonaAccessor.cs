using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// SEAM (SPEC F35.2, F35.5) — the thin Core-visible accessor between
/// <c>GenWave.Orchestration</c> (which cannot see the Host's <c>IOptionsMonitor&lt;StationOptions&gt;</c>
/// or <see cref="IPersonaStore"/> directly) and the Host's live station configuration + persona
/// storage. Mirrors <see cref="IStationScopeProvider"/>'s seam shape one level up: both sides of the
/// boundary depend on this one interface instead of inventing separate idioms.
///
/// Implementations MUST re-evaluate the active persona id fresh on every call — never cache it in a
/// field — so a live activate/deactivate (the F19 overlay write) is visible to the very next render
/// with no process restart.
/// </summary>
public interface IActivePersonaAccessor
{
    /// <summary>
    /// Resolves the currently active persona, or <see langword="null"/> when none is active.
    ///
    /// NEVER throws (F35.5 — the render path this feeds must always get an answer, never a
    /// stall): an absent/zero active id resolves to <see langword="null"/> with no log (the
    /// default "no persona" state, not a degradation); a non-zero id with no matching row, or any
    /// underlying store failure, both degrade to <see langword="null"/> with a WARN logged by the
    /// implementation.
    /// </summary>
    Task<Persona?> ResolveAsync(CancellationToken ct);

    /// <summary>
    /// Resolves the active persona's card definition (SPEC F71.1, F71.3, F71.7) — the
    /// quirks/corrections/soul document, this seam's F71 counterpart to <see cref="ResolveAsync"/>'s
    /// legacy shape. Same never-throws contract: no active persona, a card-less row, or any store
    /// fault all resolve to <see langword="null"/>, never a stall.
    ///
    /// Default-implemented (not abstract) so this Q3 addition to a published MIT contract
    /// (<c>GenWave.Abstractions</c>) stays strictly additive — every implementer that predates F71
    /// (a test double, or a host built against an older SDK version) keeps compiling unchanged and
    /// simply reports "no card" until it opts in with a real override.
    /// </summary>
    Task<PersonaCard?> ResolveCardAsync(CancellationToken ct) => Task.FromResult<PersonaCard?>(null);

    /// <summary>
    /// Synchronous, in-memory read of the active persona id (SPEC F84.6, STORY-215) — no store round
    /// trip, unlike <see cref="ResolveAsync"/>. Exists for a caller that sits on a hot path which must
    /// return promptly (<see cref="IStationEventSink"/>'s own contract) and so cannot await a
    /// persona-store read to capture "who is on air right now": the booth log stamps
    /// this value AT AIR TIME rather than resolving it later when the row is eventually persisted,
    /// which is exactly what "stamped at air time, never inferred after the fact" requires.
    ///
    /// Same never-throws, same null contract as <see cref="ResolveAsync"/>: <see langword="null"/>
    /// for the default "no persona" state. Default-implemented as "no persona" for the same additive
    /// reason <see cref="ResolveCardAsync"/> is — every pre-F84.6 implementer (a test double, an
    /// older SDK consumer) keeps compiling unchanged and simply reports "no active persona" until it
    /// opts in with a real override.
    /// </summary>
    long? ActivePersonaId => null;

    /// <summary>
    /// Synchronous, in-memory read of a cached persona display name (SPEC F93.1, F93.4, STORY-244,
    /// PLAN T125) — no store round trip, unlike <see cref="ResolveAsync"/>. Exists for the spectator
    /// now-playing poll, which (SPEC F93.4) must issue no DB or engine call of its own: the poll
    /// looks up <c>CachingScheduleResolver.TryGetCurrent()?.PersonaId</c> and passes it here rather
    /// than calling <see cref="ResolveAsync"/> itself.
    ///
    /// <para>
    /// An implementation is expected to populate this memo OPPORTUNISTICALLY, as a side effect of its
    /// own <see cref="ResolveAsync"/> succeeding for that id — never by querying on this call. It is
    /// therefore only as fresh as the last time that id was resolved through the ordinary
    /// orchestration path (lead-in/back-announce persona resolution, or a bound
    /// <c>IPersonaPickProvider</c>'s own resolve) — answers <see langword="null"/> for an id never
    /// yet seen that way, including the process-boot window before the first such resolve.
    /// </para>
    ///
    /// <para>
    /// Default-implemented as "not cached" (null) for the same additive reason as
    /// <see cref="ResolveCardAsync"/> and <see cref="ActivePersonaId"/>: every pre-F93 implementer (a
    /// test double, an older SDK consumer) keeps compiling unchanged and simply reports "name not
    /// cached" until it opts in with a real override.
    /// </para>
    /// </summary>
    string? TryGetCachedName(long personaId) => null;
}
