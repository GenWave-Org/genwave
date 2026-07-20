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
}
