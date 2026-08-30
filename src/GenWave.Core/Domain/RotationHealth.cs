namespace GenWave.Core.Domain;

/// <summary>
/// The <c>GET /api/status</c> rotation aggregate (SPEC F149.5, STORY-368, PLAN T371) —
/// <see cref="Abstractions.IMediaRotationSink.GetRotationHealthAsync"/>'s own return shape. Every
/// count is over the SAME posture <see cref="Abstractions.IMediaRotationSink.GetNeverAiredCountAsync"/>
/// already established: playable rows (<c>Catalog.MediaRepository.PlayablePredicate</c>'s text,
/// mirrored in <c>MediaRotationRepository</c>) within the station's own rotation scope, safe-scope
/// rows excluded (gh-#99 — a "Please Stand By" loop or station ID is never rotation).
/// </summary>
/// <param name="Playable">
/// The total playable-row population every other count here is drawn from — the dashboard tile's
/// own denominator ("N of <see cref="Playable"/> never aired"). Not itself part of SPEC F149.5's
/// original four-field <c>rotation</c> shape; added here (and passed through on the wire) because no
/// existing <c>GET /api/status</c> figure answers "how many playable rows in the STATION's own
/// rotation scope" — <c>safeScope.playable</c> answers a different question (is the SAFE loop
/// itself populated), scoped to <c>Station:SafeScope:LibraryIds</c> rather than
/// <c>IStationScopeProvider.Current</c>.
/// </param>
/// <param name="NeverAired">
/// Playable rows carrying no <c>library.media_rotation</c> row at all, or whose <c>play_count</c> is
/// still 0 — the same figure <see cref="Abstractions.IMediaRotationSink.GetNeverAiredCountAsync"/>
/// answers, now scoped to the station's rotation <c>LibraryScope</c> too.
/// </param>
/// <param name="AiredOnce">Playable rows whose <c>play_count</c> is exactly 1.</param>
/// <param name="NotAiredDays90">
/// Playable rows whose <c>last_aired_at</c> is more than 90 days old. A row that has never aired at
/// all is NOT counted here too — it is <see cref="NeverAired"/>'s figure alone, since a null
/// <c>last_aired_at</c> never satisfies the "&lt; now() − 90 days" comparison.
///
/// <para>
/// <b>T371 review LOW-3 — this figure OVERLAPS <see cref="AiredOnce"/>.</b> A row aired exactly
/// once, 91 days ago, is counted in BOTH: <c>play_count = 1</c> satisfies <see cref="AiredOnce"/>
/// and <c>last_aired_at &lt; now() − 90 days</c> independently satisfies this one — the two SQL
/// predicates are not mutually exclusive by construction (only <see cref="NeverAired"/> is disjoint
/// from both, since a null <c>last_aired_at</c>/<c>play_count</c> can never also be 1 or old). The
/// four counts therefore do NOT sum to <see cref="Playable"/> in general; the dashboard tile only
/// ever reads them as independent facts ("N aired once", "M stale"), never as an implied partition.
/// SPEC rider owed (F149.5 doesn't currently say this) — flagged for /document.
/// </para>
/// </param>
/// <param name="RotationSince">
/// The ledger's own epoch (SPEC F149.3) — <see langword="null"/> only on a pre-Gardener install
/// whose migration has never run, mirroring <see cref="Abstractions.IMediaRotationSink.GetRotationSinceAsync"/>.
/// </param>
public sealed record RotationHealth(long Playable, long NeverAired, long AiredOnce, long NotAiredDays90, DateTimeOffset? RotationSince);
