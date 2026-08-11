using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F115.2 (STORY-306, PLAN T241) — the ONE identity chokepoint every on-air identity concern
/// resolves through, so ARCHITECTURE.md's "📛 Dayparting: named shows" design-for-change spine holds:
/// the deferred schedulable-bundle slice widens exactly this one function's own <see cref="Resolve"/>
/// logic, and every v1 consumer — reading identity off <see cref="GenWave.Abstractions.Playout.OnAirSnapshot"/>,
/// which <see cref="ScheduleResolver"/> builds through this type — is untouched by construction.
///
/// <para>
/// <b>v1 rule (implemented here): BLOCK-LEVEL PERSONA ONLY.</b> <see cref="Resolve"/> always returns
/// <paramref name="block"/>'s own <c>PersonaId</c> — <paramref name="show"/> is never consulted for it.
/// This is not an oversight: <see cref="ShowSummary"/> structurally carries no <c>persona_id</c>/
/// <c>envelope</c> member at all (SPEC F115.2's dormant-columns-unread pin — see that type's own
/// remarks), so there is nothing on <paramref name="show"/> this function COULD read even if it tried.
/// </para>
///
/// <para>
/// <b>Future rule (recorded, NOT implemented here):</b> once the deferred schedulable-bundle slice
/// widens <c>station.show</c>'s dormant <c>persona_id</c>/<c>envelope</c> columns into a real reader,
/// the effective persona/envelope becomes <c>block ?? show ?? none</c> — block always wins. That
/// widening touches only this function's own body: the type it takes for <paramref name="show"/> would
/// grow the bundle fields, but <see cref="ScheduleResolver"/> and every downstream consumer of
/// <see cref="GenWave.Abstractions.Playout.OnAirSnapshot"/> stay diff-free.
/// </para>
///
/// <para>
/// <paramref name="show"/> is taken as its own parameter rather than reached via
/// <paramref name="block"/>'s own <see cref="ScheduleSegment.Show"/> property deliberately (Law of
/// Demeter): this function only ever needs the two pieces of already-resolved state its caller hands
/// it, never <see cref="ScheduleSegment"/>'s full shape — the same decoupling that lets a future
/// specials rung (PLAN T258, "dated rows shadow the grid") substitute a DIFFERENT resolved show for a
/// given block without this function changing at all.
/// </para>
///
/// <para>
/// Envelope resolution is deliberately OUT of this function's scope: <see cref="ScheduleResolver.Resolve"/>'s
/// own <c>BuildSegmentEnvelope</c> already implements the unrelated "segment fields ?? station-default"
/// fallback (SPEC F91.4) — that chain never involves a show today, so moving it here would be a diff
/// with no behavior change. The bundle slice's own envelope widening lands in <see cref="Resolve"/>
/// alongside the persona widening described above, not before.
/// </para>
/// </summary>
public sealed record EffectiveAssignment(long? PersonaId, ShowSummary? Show)
{
    /// <summary>Resolves <paramref name="block"/>/<paramref name="show"/> into the identity
    /// <see cref="ScheduleResolver"/> folds into every <see cref="GenWave.Abstractions.Playout.OnAirSnapshot"/>
    /// it builds — see this type's own remarks for the v1-vs-future rule.</summary>
    public static EffectiveAssignment Resolve(ScheduleSegment block, ShowSummary? show) => new(block.PersonaId, show);
}
