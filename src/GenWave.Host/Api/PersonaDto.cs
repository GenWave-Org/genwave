namespace GenWave.Host.Api;

/// <summary>
/// Wire shape for a persona row (SPEC F35.4, F90.7): <c>id</c>/<c>name</c>/<c>backstory</c>/
/// <c>style</c>/<c>voice</c>, plus <c>importedFrom</c>/<c>importedAt</c>. Mirrors
/// <see cref="Auth.LibraryDto"/>'s minimal-fields discipline — the spec's documented shape for
/// <c>GET /api/personas</c> and the POST/PATCH response bodies omits <c>created_at</c>/
/// <c>updated_at</c>, so this DTO doesn't echo every <c>Persona</c> column. <c>ImportedFrom</c>/
/// <c>ImportedAt</c> are the one exception (SPEC F90.7): the Admin UI's provenance badge
/// ("Imported · &lt;source&gt; · &lt;date&gt;", T105) needs them on the same list/detail projection
/// every other persona field rides on — both serialize as <c>null</c> for an authored-in-place
/// persona. Admin-plane only: the spectator surface never projects persona/DJ identity beyond a
/// display name, so it never gains these fields (disclosure law, F62.9).
/// </summary>
public sealed record PersonaDto(
    long Id, string Name, string Backstory, string Style, string Voice,
    string? ImportedFrom, DateTime? ImportedAt);
