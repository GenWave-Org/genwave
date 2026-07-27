namespace GenWave.Host.Api;

/// <summary>
/// 200 response body for <c>GET /api/catalog/index</c> (SPEC F90.2, F90.4) — ALWAYS 200 once the
/// surface itself is enabled (see <see cref="CatalogController"/>'s own remarks for the disabled
/// case, a bare 404 instead). <see cref="Unreachable"/> is the graceful-degraded signal
/// ARCHITECTURE.md's Persona Catalog section asks for verbatim ("Offline/unreachable = a graceful
/// empty-state, never an error page") — mirrors the house idiom already used for a degraded
/// UI-facing surface (<c>SpectatorTrackNowPlaying.Listeners</c>, <c>StatusController</c>'s
/// <c>llm.lastOutcome</c>: a null/flagged field embedded in a 200, not a non-2xx status the Admin
/// UI's generic fetch wrapper would have to special-case into an error toast). A cold cache with no
/// prior success and a hostile/rejected upstream index both collapse to this SAME shape — the Admin
/// UI needs "nothing to show right now", not to distinguish why.
/// </summary>
/// <param name="Entries">The shelf listing, or <see langword="null"/> when <see cref="Unreachable"/>.</param>
/// <param name="FetchedAt">
/// When THIS listing was originally fetched (never "now" — a stale-served cache keeps its original
/// stamp, SPEC F90.4). <see langword="null"/> when <see cref="Unreachable"/>.
/// </param>
/// <param name="Unreachable">
/// <see langword="true"/> when no usable listing exists right now (cold cache + failed/rejected
/// origin) — the Admin UI renders its empty state, never an error page.
/// </param>
public sealed record CatalogIndexResponse(
    IReadOnlyList<CatalogShelfEntryDto>? Entries, DateTimeOffset? FetchedAt, bool Unreachable);
