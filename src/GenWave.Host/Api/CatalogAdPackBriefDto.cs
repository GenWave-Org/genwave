namespace GenWave.Host.Api;

/// <summary>
/// One brief on an ad-pack entry's detail projection (SPEC F162.2, STORY-393, PLAN T405) — the wire
/// projection of one <see cref="Catalog.CatalogAdPackBrief"/>, read off the pack's own fetched,
/// hash-verified <c>.ad-pack.json</c> manifest (<see cref="CatalogEntryResponse.AdPackBriefs"/>'s own
/// remarks). READ-ONLY on this wire: the shelf's own <c>AdPackDetailPanel</c> only ever LISTS these
/// briefs for review — nothing here is editable, and nothing is written anywhere until
/// <c>POST /api/ad-packs/{slug}/install</c> is explicitly confirmed.
/// </summary>
/// <param name="Brand">The brand this brief is about — never null on a validated manifest (<see cref="Catalog.CatalogAdPackManifestSerializer"/>'s own required field).</param>
/// <param name="Premise">The brand's premise hint, or <see langword="null"/> when the pack declares none.</param>
/// <param name="Tone">The brand's tone hint, or <see langword="null"/> when the pack declares none.</param>
/// <param name="Structure">The brand's structure hint, or <see langword="null"/> when the pack declares none.</param>
public sealed record CatalogAdPackBriefDto(string Brand, string? Premise, string? Tone, string? Structure);
