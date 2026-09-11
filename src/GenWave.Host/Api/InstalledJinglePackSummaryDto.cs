namespace GenWave.Host.Api;

/// <summary>
/// One row of <c>GET /api/jingle-packs</c> (gh-#718) — <see cref="InstalledPackSummaryDto"/>'s shelf
/// contract (<see cref="Slug"/> + <see cref="PackName"/>, same fallback rule, see that record's
/// remarks) plus the ONE fact a jingle pack has that a voice pack does not: the catalog library its
/// assets were installed into.
///
/// <para>
/// <b>Why the library is on the wire.</b> An unnamed <c>GET /api/media</c> browse is scoped to the
/// station's rotation libraries (SPEC F23.2); the packs' own library (<c>Ads:LibraryName</c>,
/// station imaging rather than rotation) is normally NOT in that scope, so the spot wizard's
/// Background music picker asked for beds and got an empty list while the station's own render
/// picked one anyway (gh-#718). The picker now names each installed pack's library through the
/// explicit <c>library-id=</c> browse (F23.2's named-library override) — and this is where it
/// learns the id.
/// </para>
/// </summary>
/// <param name="Slug">The catalog entry's own slug — unique across every installed jingle pack.</param>
/// <param name="PackName">The manifest's own display name, or <paramref name="Slug"/> when the stored
/// <c>definition</c> failed to re-parse (never a dropped row).</param>
/// <param name="LibraryId">The id of the library the pack's assets were installed into — the same
/// <c>Ads:LibraryName</c> lookup <c>JinglePackController.Install</c> itself resolves at install
/// time. <c>null</c> only when that library no longer resolves by name (renamed or deleted since);
/// the picker then falls back to the station-scoped browse rather than guessing an id.</param>
public sealed record InstalledJinglePackSummaryDto(string Slug, string PackName, long? LibraryId);
