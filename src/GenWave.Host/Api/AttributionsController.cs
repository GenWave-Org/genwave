using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Host.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GenWave.Host.Api;

/// <summary>
/// Attribution surface (SPEC F169.1/F169.2, STORY-404, PLAN T419) — iterates every installed
/// jingle-pack/font-pack/voice-pack row and projects its credits for the About page epic (gh-#16).
/// There is deliberately no separate <c>station.attribution</c> table: every credit line here is
/// re-derived, on every request, from the pack's own stored <c>definition</c> jsonb (db/45's own
/// header remarks) — a mirror table would drift the moment a pack's manifest and its own attribution
/// copy disagreed.
///
/// <para>
/// <b>Group order is fixed</b> (<c>jingle-pack</c>, <c>font-pack</c>, <c>voice-pack</c> — F169.2's own
/// listing order), not alphabetical and not install-time: a caller rendering this straight into a page
/// section list gets a stable layout across requests without its own sort. Packs within a group are
/// ordered by slug; an empty group is omitted entirely (STORY-404 AC2) rather than sent with a bare
/// <c>[]</c> packs array.
/// </para>
///
/// <para>
/// <b>Per-kind projection</b> (STORY-404 AC3-AC5): a jingle pack's CC-BY assets each surface as their
/// own line (title/creator/sourceUrl/license byte-for-byte off the asset's own
/// <see cref="CatalogJinglePackAttribution"/>); every CC0 asset in the SAME pack collapses to exactly
/// ONE aggregate line named after the pack itself, with no creator/source (SPEC's "many small CC0
/// clips" case would otherwise flood this list one line per file for information nobody credits
/// per-file anyway) — a mixed pack yields its CC-BY lines first, then that one CC0 line. A font pack
/// contributes one line at the pack level (its stored <c>license</c>/<c>sourceUrl</c>, no creator — a
/// font manifest carries no creator field, SPEC F104.1). A voice pack contributes exactly one line
/// naming the pack, license <c>"Synthetic blend"</c>, no creator/source — synthetic voices have no
/// per-voice human credit to surface (AC5), unlike a jingle asset's real recording.
/// </para>
///
/// <para>
/// <b>Malformed definition = skip, never 500</b>: any of the three hardened parsers
/// (<see cref="CatalogJinglePackManifestSerializer.Deserialize"/>,
/// <see cref="CatalogFontManifestSerializer.Deserialize"/>,
/// <see cref="CatalogVoicePackManifestSerializer.Deserialize"/>) returning <see langword="null"/> for
/// a stored row (should never happen past their own install-time validation, but this endpoint reads
/// the same column a hand-edited row or a future migration could still corrupt) drops that ONE pack
/// from its group, logs one <see cref="ILogger.LogWarning(string, object[])"/> naming the kind and
/// slug, and continues — every other installed pack still renders.
/// </para>
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Curation)]
public sealed class AttributionsController(
    IJinglePackStore jinglePackStore,
    IFontPackStore fontPackStore,
    IVoicePackStore voicePackStore,
    ILogger<AttributionsController> logger) : ControllerBase
{
    const string SyntheticBlendLicense = "Synthetic blend";
    const string Cc0License = "CC0";
    const string CcByLicense = "CC-BY";

    /// <summary>GET /api/attributions (SPEC F169.2) — see this controller's own remarks for the full
    /// projection contract.</summary>
    [HttpGet("attributions")]
    public async Task<IActionResult> GetAttributions(CancellationToken ct)
    {
        var jinglePacks = await BuildJinglePackGroupAsync(ct);
        var fontPacks = await BuildFontPackGroupAsync(ct);
        var voicePacks = await BuildVoicePackGroupAsync(ct);

        var groups = new List<AttributionGroupDto>();
        if (jinglePacks.Count > 0)
            groups.Add(new AttributionGroupDto("jingle-pack", jinglePacks));
        if (fontPacks.Count > 0)
            groups.Add(new AttributionGroupDto("font-pack", fontPacks));
        if (voicePacks.Count > 0)
            groups.Add(new AttributionGroupDto("voice-pack", voicePacks));

        return Ok(new AttributionsResponse(groups));
    }

    async Task<IReadOnlyList<AttributionPackDto>> BuildJinglePackGroupAsync(CancellationToken ct)
    {
        var rows = await jinglePackStore.ListAsync(ct);
        var packs = new List<AttributionPackDto>(rows.Count);

        foreach (var row in rows.OrderBy(r => r.Slug, StringComparer.Ordinal))
        {
            var manifest = CatalogJinglePackManifestSerializer.Deserialize(row.DefinitionJson);
            if (manifest is null)
            {
                LogMalformed("jingle-pack", row.Slug);
                continue;
            }

            var lines = new List<AttributionLineDto>();
            foreach (var asset in manifest.Assets)
            {
                if (string.Equals(asset.License, CcByLicense, StringComparison.Ordinal))
                {
                    lines.Add(new AttributionLineDto(
                        asset.Title, asset.Attribution?.Creator, asset.Attribution?.SourceUrl, CcByLicense));
                }
            }

            if (manifest.Assets.Any(a => string.Equals(a.License, Cc0License, StringComparison.Ordinal)))
                lines.Add(new AttributionLineDto(manifest.PackName, null, null, Cc0License));

            packs.Add(new AttributionPackDto(row.Slug, manifest.PackName, lines));
        }

        return packs;
    }

    async Task<IReadOnlyList<AttributionPackDto>> BuildFontPackGroupAsync(CancellationToken ct)
    {
        var rows = await fontPackStore.GetAllAsync(ct);
        var packs = new List<AttributionPackDto>(rows.Count);

        foreach (var row in rows.OrderBy(r => r.Slug, StringComparer.Ordinal))
        {
            var manifest = CatalogFontManifestSerializer.Deserialize(row.Definition);
            if (manifest is null)
            {
                LogMalformed("font-pack", row.Slug);
                continue;
            }

            var line = new AttributionLineDto(manifest.Family, null, manifest.SourceUrl, manifest.License);
            packs.Add(new AttributionPackDto(row.Slug, manifest.Family, [line]));
        }

        return packs;
    }

    async Task<IReadOnlyList<AttributionPackDto>> BuildVoicePackGroupAsync(CancellationToken ct)
    {
        var rows = await voicePackStore.ListAsync(ct);
        var packs = new List<AttributionPackDto>(rows.Count);

        foreach (var row in rows.OrderBy(r => r.Slug, StringComparer.Ordinal))
        {
            var manifest = CatalogVoicePackManifestSerializer.Deserialize(row.DefinitionJson);
            if (manifest is null)
            {
                LogMalformed("voice-pack", row.Slug);
                continue;
            }

            var line = new AttributionLineDto(manifest.PackName, null, null, SyntheticBlendLicense);
            packs.Add(new AttributionPackDto(row.Slug, manifest.PackName, [line]));
        }

        return packs;
    }

    void LogMalformed(string kind, string slug) =>
        logger.LogWarning(
            "Attributions endpoint skipped {Kind} pack {Slug}: its stored definition failed to re-parse",
            LogSanitize.Strip(kind), LogSanitize.Strip(slug));
}
