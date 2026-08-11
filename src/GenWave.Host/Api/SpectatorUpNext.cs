namespace GenWave.Host.Api;

/// <summary>
/// The "who's on next" projection nested under <c>GET /spectator/api/now-playing</c>'s <c>onAir</c>
/// shapes (SPEC F93.2, STORY-244, PLAN T125): exactly one upcoming segment — never a deeper
/// lookahead. <see cref="Dj"/> is the incoming segment's persona display name, or null for a
/// music-only segment; the page renders that as the fixed label "Nonstop music".
/// <para>
/// SAME-PERSONA-AND-SAME-SHOW RULE (decided at T125 build, extending the F92.3 ceremony ruling —
/// "a boundary whose outgoing and incoming persona are the SAME airs no handoff ceremony" — to
/// this public display; WIDENED at PLAN T251 review per SPEC F116.2): <see cref="SpectatorController"/>
/// collapses the WHOLE property to <see langword="null"/>, rather than reporting it, only when
/// BOTH the resolver's upcoming persona id AND upcoming show id equal the current ones. A
/// same-DJ-same-show boundary (the F91.6 seeded grid's own midnight roll being the motivating
/// case) is not a change a listener needs announced — but F116.2 rules a same-persona
/// DIFFERENT-show boundary a REAL on-air event (ceremony airs one piece, styled as a transition),
/// so this property must still report it: collapsing on persona alone would silently disagree
/// with what listeners actually hear. The identical two-part comparison also naturally collapses a
/// gap rolling into another music-only segment (no persona/show before, none after — nothing
/// changes either way) and a schedule with no boundary at all (an empty grid never has anything
/// upcoming to report) — no special-casing needed for either, since both compare as "same" (null
/// equals null on both fields).
/// </para>
/// </summary>
/// <param name="StartsAt">The boundary instant the upcoming segment takes the air.</param>
/// <param name="Dj">The upcoming segment's persona display name, or null for music-only.</param>
/// <param name="Show">
/// The upcoming segment's show identity, NAME ONLY (SPEC F116.4, STORY-311, PLAN T251), or
/// <see langword="null"/> for an unnamed block — read straight off
/// <see cref="GenWave.Abstractions.Playout.OnAirSnapshot.NextSegment"/>'s own
/// <see cref="GenWave.Core.Domain.ScheduleSegment.Show"/>, the same resolver-sourced identity
/// <see cref="SpectatorController.GetNowPlaying"/> reads the current show from — never a store
/// read on the poll path (F93.4). IS subject to the same-persona-and-same-show collapse above —
/// a same-persona different-show boundary is exactly the case that rule was widened to keep this
/// field populated for (F116.2); it is only the whole <see cref="SpectatorUpNext"/> property
/// collapsing (both fields together) that this field can never independently opt out of.
/// </param>
public sealed record SpectatorUpNext(DateTimeOffset StartsAt, string? Dj, SpectatorUpNextShow? Show);
