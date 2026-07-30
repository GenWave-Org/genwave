namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for a persona row (SPEC F35.4, F90.7, F94.2): <c>id</c>/<c>name</c>/<c>backstory</c>/
/// <c>style</c>/<c>voice</c>, plus <c>slug</c>/<c>importedFrom</c>/<c>importedAt</c>. Mirrors
/// <see cref="Auth.LibraryDto"/>'s minimal-fields discipline — the spec's documented shape for
/// <c>GET /api/personas</c> and the POST/PATCH response bodies omits <c>created_at</c>/
/// <c>updated_at</c>, so this DTO doesn't echo every <c>Persona</c> column. <c>ImportedFrom</c>/
/// <c>ImportedAt</c> are the one exception (SPEC F90.7): the Admin UI's provenance badge
/// ("Imported · &lt;source&gt; · &lt;date&gt;", T105) needs them on the same list/detail projection
/// every other persona field rides on — both serialize as <c>null</c> for an authored-in-place
/// persona.
///
/// <c>Slug</c> (PLAN T128 review fix) is the server's own <see cref="Persona.Slug"/> — the ONLY
/// value the Admin UI may use to build a <c>GET/POST /api/personas/{slug}/export|import</c> link for
/// a SAVED row. A client-side re-slugify of <c>name</c> is not a legal substitute: an imported
/// persona's slug can diverge from a fresh slugify of its current name until the next admin edit
/// (see <see cref="Persona.Slug"/>'s own remarks), which 404'd the Export action inside the Fire
/// modal's export-first parachute — silently, since the parachute's own gate is click-based, not
/// response-based.
///
/// Admin-plane only: the spectator surface never projects persona/DJ identity beyond a display
/// name, so it never gains any of these fields (disclosure law, F62.9).
///
/// <para>
/// <c>Soul</c>/<c>Quirks</c>/<c>Lore</c> (gh-#256) surface the F71.1 persona-card fields a
/// catalog-hired DJ's narrative actually lives in — its legacy backstory/style columns are
/// deliberately blank (<c>PersonaImportRepository</c>), which left the editor showing an empty
/// Backstory and no Style for every hired persona. All three are always serialized: <c>""</c>/empty
/// lists for a persona whose card carries none (or whose row still holds the migration sentinel),
/// never an absent key.
/// </para>
/// </summary>
public sealed record PersonaDto(
    long Id, string Name, string Backstory, string Style, string Voice, string Slug,
    string? ImportedFrom, DateTime? ImportedAt,
    string Soul, IReadOnlyList<string> Quirks, IReadOnlyList<string> Lore);
