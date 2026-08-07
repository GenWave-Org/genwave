namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/themes/{slug}/save-as-own</c> (SPEC F104.13, STORY-287, PLAN T207) —
/// the save-as-own sibling of <see cref="ThemeImportResponse"/>. Narrower still than that type:
/// <see cref="ThemesSaveAsOwnController.SaveAsOwn"/> always stamps <c>imported_from</c>
/// <see langword="null"/> (the reserved authored provenance, SPEC F104.13), so neither an
/// <c>ImportedFrom</c> nor an <c>ImportedAt</c> field would ever carry a meaningful value here — see
/// <see cref="GenWave.MediaLibrary.Station.ThemeRepository.UpsertAsync"/>'s own remarks for why
/// <c>imported_at</c> stays <see langword="null"/> too rather than a fabricated "saved at" stamp
/// dressed up as import provenance.
/// </summary>
/// <param name="Slug">The route slug — the upsert key, and what the saved theme now resolves under
/// (<c>ThemeCatalog</c>, <c>Station:Theme</c>).</param>
/// <param name="Name">The manifest's own display name.</param>
public sealed record ThemeSaveAsOwnResponse(string Slug, string Name);
