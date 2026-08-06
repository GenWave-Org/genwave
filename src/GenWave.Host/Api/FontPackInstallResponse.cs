namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/fonts/{slug}/install</c> (SPEC F104.5, STORY-282, PLAN T199) — the
/// font-kind sibling of <see cref="ThemeImportResponse"/>. Narrower than a full library row
/// (<c>GenWave.Core.Domain.FontPack</c>): the library page (PLAN T203) is the future full-detail
/// read, so this only echoes back what the install itself decided, mirroring
/// <see cref="ThemeImportResponse"/>'s own "only what the accepted write genuinely hands back"
/// posture.
/// </summary>
/// <param name="Slug">The route slug — the upsert key, and the catalog entry this pack installed
/// from.</param>
/// <param name="Family">The manifest's own CSS family name.</param>
/// <param name="Faces">The bare <c>/fonts/&lt;file&gt;</c> filenames just installed, in manifest
/// order.</param>
/// <param name="ImportedFrom">The provenance stamp written — always <paramref name="Slug"/> itself
/// (SPEC F104.5): a pack has no authored-in-place or file-upload path, so this is never anything
/// else, unlike <see cref="ThemeImportResponse.ImportedFrom"/>'s own "file" alternative.</param>
public sealed record FontPackInstallResponse(string Slug, string Family, IReadOnlyList<string> Faces, string ImportedFrom);
