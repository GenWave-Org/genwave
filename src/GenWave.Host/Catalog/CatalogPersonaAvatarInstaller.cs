using System.Diagnostics;
using System.Security.Cryptography;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Images;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Catalog;

/// <summary>
/// Installs a catalog persona entry's OWN sidecar face onto the persona a
/// <c>POST /api/personas/{slug}/import?catalogSlug=</c> request just created/updated (SPEC F128.7,
/// STORY-334, PLAN T297) — a SECOND, independent consumer of the T291
/// <see cref="ImageNormalizeService"/> pipeline off a catalog origin, alongside
/// <c>Api.AvatarPackController</c>'s own install route. RE-VALIDATION IS NOT OPTIONAL here either
/// (that controller's own remarks, applied identically): the catalog's CI having approved this PNG
/// at publish time proves nothing about what this fetch — freshly re-resolved and re-fetched through
/// <see cref="CatalogProxyService"/>, hash-verified whether it lands fresh or out of that service's
/// own cache — actually returns, so this class trusts nothing the operator's browser
/// already rendered in the trust modal — that earlier <c>GET /api/catalog/entries/{slug}</c> call is
/// a DIFFERENT request on a DIFFERENT trust boundary (an authenticated admin's own read), never a
/// value this write path may treat as already verified.
///
/// <para>
/// <b>THE FACE IS DECORATIVE (SPEC F128.9's placeholder posture; ruling recorded here and at SPEC
/// F128.7's own PLAN T297 note) — every failure below degrades to a faceless import, never a failed
/// one.</b> <c>Api.PersonaController.Import</c> only ever calls
/// <see cref="InstallIfPresentAsync"/> AFTER its own one transactional write
/// (<see cref="IPersonaImportStore.ImportAsync"/>) has ALREADY COMMITTED — the persona row exists
/// whether or not this method ever succeeds. Making a DJ hire depend on this station's OWN outbound
/// reach to the catalog origin a SECOND time (this call is wholly independent of whatever the import
/// request's body already carried) would put a decorative extra — a face — in the critical path of
/// something SPEC F90's trust ruling never asked to be conditional on it. Every branch below
/// therefore WARN-logs its reason, sanitized, and returns — leaving the persona exactly as faceless
/// as it would render under <see cref="PersonaAvatar"/>'s own "absent ⇒ placeholder" contract.
/// <see cref="InstallIfPresentAsync"/>'s own outer try/catch is what makes that true for every
/// REACHABLE failure too, not only the branches this class recognizes by name — an Npgsql error from
/// <see cref="IPersonaAvatarStore.UpsertAsync"/> (including a concurrent persona-delete FK race), an
/// <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/> from
/// <see cref="ImageNormalizeService"/>'s own temp-file handling, or any other unanticipated escape all
/// WARN-log and return the same as a recognized branch — mirrors
/// <c>MediaLibrary.Station.PersonaCardMigrator.RunAsync</c>/<see cref="CatalogHttpFetcher"/>'s own
/// idiom exactly. Only caller cancellation (<see cref="OperationCanceledException"/> off this method's
/// own <c>ct</c>) is ever allowed through — never handing <c>Import</c>'s own caller any OTHER signal
/// that this ran at all.
/// </para>
/// </summary>
public sealed class CatalogPersonaAvatarInstaller(
    CatalogProxyService catalogProxyService,
    IPersonaAvatarStore personaAvatarStore,
    ImageNormalizeService imageNormalizeService,
    ILogger<CatalogPersonaAvatarInstaller> logger) : ICatalogPersonaAvatarInstaller
{
    // 32 lowercase hex chars = 16 bytes = 128 bits (SPEC F129.1) — the SAME shape
    // Api.PersonaAvatarController.TokenLength mints, independently: that type's own TOKEN ENTROPY
    // remarks already explain why an avatar token and this installer's own capability space never
    // need to share a constant, only the same magnitude — repeated here rather than referenced, for
    // the identical reason.
    const int TokenLength = 32;

    /// <summary>
    /// Installs <paramref name="catalogSlug"/>'s own sidecar face onto <paramref name="personaId"/>,
    /// if the entry declares one — a no-op, WARN-only no-op, for every other outcome (unreachable
    /// catalog, unknown/wrong-kind slug, a withheld/undeclared asset, or a re-validation reject). See
    /// this class's own THE FACE IS DECORATIVE remarks for why nothing here is ever caller-visible.
    /// Called ONLY when the import that just committed named a <c>catalogSlug</c> — a file-upload
    /// import never reaches this method at all (SPEC F128.7's "file import stays card-only" line;
    /// <c>Api.PersonaController.Import</c>'s own call site is the one place that gate lives).
    /// </summary>
    public async Task InstallIfPresentAsync(long personaId, string catalogSlug, CancellationToken ct)
    {
        try
        {
            var entryResult = await catalogProxyService.GetEntryAsync(catalogSlug, ct);
            if (entryResult is not CatalogEntryFetchResult.Ok { Content.Kind: CatalogEntryKind.Persona } okEntry)
            {
                // Unreachable/NotFound/HashMismatch/Oversize, or — a should-never-happen race — a
                // kind that changed out from under this slug between the browser's own review fetch
                // and this one: none of these is a coding bug, so one WARN (no exception, no
                // distinct reason) is the whole response.
                logger.LogWarning(
                    "Persona avatar skipped (catalog entry unavailable) personaId={PersonaId} catalogSlug={CatalogSlug}",
                    personaId, LogSafeText.Sanitize(catalogSlug));
                return;
            }

            var file = ResolvePersonaAvatarFile(okEntry.Content.Assets);
            if (file is null)
                return; // No sidecar face declared on this entry — nothing to install, nothing to warn about.

            var assetResult = await catalogProxyService.GetAssetAsync(catalogSlug, file, ct);
            if (assetResult is not CatalogAssetFetchResult.Ok okAsset)
            {
                logger.LogWarning(
                    "Persona avatar skipped (asset fetch failed) personaId={PersonaId} catalogSlug={CatalogSlug}",
                    personaId, LogSafeText.Sanitize(catalogSlug));
                return;
            }

            // gh-#520: NormalizeCatalogAssetAsync, not NormalizeAsync — this is the SAME
            // catalog-sourced, hash-verified-fetch situation AvatarPackController's own install route
            // is in, so it earns the identical chunk-strip fast path for an already-512×512 PNG
            // rather than paying ffmpeg's own weaker re-encode a second time; see that method's own
            // remarks for the full reasoning.
            var normalized = await imageNormalizeService.NormalizeCatalogAssetAsync(okAsset.Bytes, ct);
            switch (normalized)
            {
                case ImageNormalizeResult.Success success:
                    // Persist the entry's own re-resolved Slug (okEntry.Content.Slug), not the
                    // caller-typed catalogSlug parameter — the SAME T295/T296 canonicalization
                    // principle PersonaAvatarController.ApplyFromPack already applies to a pack's own
                    // Slug (see that call site's own CANONICAL remarks). GetEntryAsync resolved this
                    // slug to a real entry above, so the two are provably byte-identical today; this
                    // records what the entry actually IS rather than merely echoing this call's own
                    // input, the same way that sibling write path already does.
                    await personaAvatarStore.UpsertAsync(
                        new PersonaAvatarInput(
                            personaId, success.Bytes, success.Sha256, GenerateToken(),
                            PersonaAvatarSource.Catalog, okEntry.Content.Slug),
                        ct);
                    logger.LogInformation(
                        "Persona avatar installed from catalog personaId={PersonaId} catalogSlug={CatalogSlug}",
                        personaId, LogSafeText.Sanitize(catalogSlug));
                    return;

                case ImageNormalizeResult.Failure failure:
                    logger.LogWarning(
                        "Persona avatar skipped (server-side re-validation failed) personaId={PersonaId} catalogSlug={CatalogSlug} reason={Reason}",
                        personaId, LogSafeText.Sanitize(catalogSlug), failure.Reason);
                    return;

                default:
                    throw new UnreachableException($"Unhandled {nameof(ImageNormalizeResult)} case.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // THE FACE IS DECORATIVE (this class's own class-level remarks): every reachable escape
            // — Npgsql from UpsertAsync (including a concurrent persona delete's FK race),
            // IOException/UnauthorizedAccessException from ImageNormalizeService's own temp-file
            // handling, or the should-never-happen UnreachableException above — degrades to the SAME
            // WARN-and-return every other branch in this method already uses, never a caller-visible
            // exception. Mirrors PersonaCardMigrator.RunAsync/CatalogHttpFetcher.FetchAsync's own
            // idiom exactly.
            logger.LogWarning(ex,
                "Persona avatar install failed personaId={PersonaId} catalogSlug={CatalogSlug}",
                personaId, LogSafeText.Sanitize(catalogSlug));
        }
    }

    /// <summary>Mirrors <c>Api.CatalogController.ResolvePersonaAvatarFile</c> exactly (SPEC F128.2,
    /// PLAN T292) — a persona entry's <see cref="CatalogEntryContent.Assets"/> is already
    /// index-validated to hold AT MOST one element for this kind
    /// (<see cref="CatalogIndexValidator.TryValidatePersonaAvatarAsset"/>), so a single lookup is the
    /// whole job; kept as its own small copy rather than a shared reference across the Api/Catalog
    /// boundary for the same reason as this class's own TokenLength — the two call sites' own shape
    /// is what has to match, not a shared symbol.</summary>
    static string? ResolvePersonaAvatarFile(IReadOnlyList<CatalogAssetRef> assets) =>
        assets.Count == 1 ? Path.GetFileName(assets[0].Path) : null;

    /// <summary>128-bit cryptographically random hex, freshly minted for every install — mirrors
    /// <c>Api.PersonaAvatarController.GenerateToken</c>'s own idiom (see this class's own TokenLength
    /// remarks for why the two stay independent).</summary>
    static string GenerateToken() => RandomNumberGenerator.GetHexString(TokenLength, lowercase: true);
}
