namespace GenWave.Host.Api;

/// <summary>
/// The "who's on next" projection nested under <c>GET /spectator/api/now-playing</c>'s <c>onAir</c>
/// shapes (SPEC F93.2, STORY-244, PLAN T125): exactly one upcoming segment — never a deeper
/// lookahead. <see cref="Dj"/> is the incoming segment's persona display name, or null for a
/// music-only segment; the page renders that as the fixed label "Nonstop music".
/// <para>
/// SAME-PERSONA RULE (decided at T125 build, extending the F92.3 ceremony ruling — "a boundary
/// whose outgoing and incoming persona are the SAME airs no handoff ceremony" — to this public
/// display): <see cref="SpectatorController"/> collapses the WHOLE property to
/// <see langword="null"/>, rather than reporting it, whenever the resolver's upcoming persona id
/// equals the current one. A same-DJ boundary (the F91.6 seeded grid's own midnight roll being the
/// motivating case) is not a change a listener needs announced. The identical comparison also
/// naturally collapses a gap rolling into another music-only segment (no persona before, none
/// after — nothing changes either way) and a schedule with no boundary at all (an empty grid never
/// has anything upcoming to report) — no special-casing needed for either, since both compare as
/// "same" (null equals null).
/// </para>
/// </summary>
/// <param name="StartsAt">The boundary instant the upcoming segment takes the air.</param>
/// <param name="Dj">The upcoming segment's persona display name, or null for music-only.</param>
public sealed record SpectatorUpNext(DateTimeOffset StartsAt, string? Dj);
