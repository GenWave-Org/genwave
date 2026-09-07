namespace GenWave.Host.Api;

/// <summary>
/// One row of <c>GET /api/voice-packs</c> or <c>GET /api/jingle-packs</c> (SPEC F164/F165 UI clauses,
/// STORY-397, PLAN T418) — just enough for the catalog shelf to mark a shelf card "Installed" and let
/// its detail panel resolve a display name, without repeating either kind's own richer install-time
/// response shape (<c>VoicePackInstallResponse</c>/<c>JinglePackInstallResponse</c>). Shared by both
/// controllers rather than given two near-identical per-kind records — the wire shape a listing route
/// needs is identical for both kinds.
///
/// <para>
/// <b><see cref="PackName"/> falls back to <paramref name="slug"/> on a re-parse failure, it never
/// drops the row.</b> Unlike <c>AttributionsController</c>'s own "skip the malformed row, log a
/// warning" posture (that endpoint's whole purpose is crediting real content, so a row it cannot
/// re-parse contributes nothing useful anyway), THIS listing route's whole purpose is telling the shelf
/// "this slug is installed" — a row a hand-edited or migration-corrupted <c>definition</c> column no
/// longer re-parses is still an installed pack occupying that slug, and hiding it here would make an
/// already-installed pack's shelf card wrongly offer an Install button over one the station cannot
/// safely reinstall (its own manifest failed the SAME hardened parser this route uses). Both
/// controllers' <c>List</c> actions DO still log — one WARN naming the slug, mirroring
/// <c>AttributionsController.LogMalformed</c>'s own shape — so a corrupt row is never silent even
/// though, unlike that endpoint, it is never dropped.
/// </para>
/// </summary>
/// <param name="Slug">The catalog entry's own slug — unique across every installed pack of that kind.</param>
/// <param name="PackName">The manifest's own display name, or <paramref name="Slug"/> when the stored
/// <c>definition</c> failed to re-parse.</param>
public sealed record InstalledPackSummaryDto(string Slug, string PackName);
