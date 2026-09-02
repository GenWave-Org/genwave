using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Catalog;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/ad-packs/{slug}/install</c> (SPEC F162.2, STORY-393, PLAN T405) — installs a
/// Dean-curated ad-pack from the Community Catalog's <c>ad-pack</c> kind into this station's own
/// brief universe (<c>station.ad_brief</c>, via <see cref="IAdBriefStore"/> — T398's own upsert
/// seam). F79 shell, POLICY PARITY WITH EVERY OTHER PACK-KIND CONTROLLER: mirrors
/// <see cref="IconPackController.Install"/> almost verbatim (that class's own remarks are this
/// route's own precedent) — the same <see cref="AdminSurfaceAttribute"/> +
/// <see cref="AuthorizationPolicies.Settings"/> pairing (a catalog-install action, not a Briefs-tab
/// editing action — <see cref="AdBriefsController"/> one file over carries
/// <see cref="AuthorizationPolicies.Curation"/> instead, the F162.1 rider's own ruling for THAT
/// surface), the same catalog-slug vocabulary (<see cref="CatalogIndexValidator.SlugSegment"/>), the
/// same "no request body, every byte fetched server-side through the guarded door" posture, and the
/// same NO-oracle <see cref="ProblemDetails"/> idioms (F15.7 — no internal detail in a body).
///
/// <para>
/// <b>NO ASSETS[] TO FETCH — SHORTER STILL THAN <see cref="IconPackController"/> (SPEC F162.2's own
/// "data only, no audio assets" words).</b> An ad-pack entry carries no binary <c>assets[]</c> at
/// all — <c>briefs[]</c> IS the manifest, already fetched, hash-verified, AND size-capped during that
/// one streamed read by <see cref="CatalogInstallShell.ResolveEntryAsync"/>'s own call into
/// <see cref="CatalogProxyService.GetEntryAsync"/>. This route never calls
/// <see cref="CatalogInstallShell.FetchAllAssetsAsync"/> — there is nothing further to fetch.
/// </para>
///
/// <para>
/// <b>Gate order.</b> Route slug format (400) → catalog kill-switch (404, bare) → resolve the entry
/// (<see cref="CatalogInstallShell.ResolveEntryAsync"/>: unknown slug or a non-ad-pack kind ⇒ 404;
/// unreachable ⇒ 503; a withheld manifest/meta ⇒ 502) → parse the manifest
/// (<see cref="CatalogAdPackManifestSerializer.Deserialize"/>: reject ⇒ 400 — that method's own class
/// remarks carry the brief-count/field-length caps this gate enforces, since this is the ONE pack
/// kind whose parsed manifest content becomes a DURABLE write) → ONE
/// <see cref="IAdBriefStore.UpsertAllAsync"/> call, the WHOLE declared brief list, inside ONE
/// transaction (SPEC F162.2 — a reinstall UPDATES every declared brief's content in place, never
/// duplicates; a failure partway through lands NOTHING, never a partially-installed pack) → 200.
/// </para>
///
/// <para>
/// <b>NO SCRIPT, NO AUDIO, NO CODE CROSSES THIS BOUNDARY (SPEC F162.2's own words) —</b> and every
/// installed brief still faces SPEC F160.3's <c>AdScriptValidator</c> at GENERATION time, exactly
/// like an owner-authored one (STORY-393 AC3): this route writes only
/// <c>brand</c>/<c>premise</c>/<c>tone</c>/<c>structure</c> — free-text prompt HINTS, never a script
/// or a rendered asset — and <c>AdSpotWorker.GenerateOneAsync</c> (GenWave.Ads) samples ANY enabled
/// brief the identical way regardless of its <c>pack_slug</c>, running the SAME real
/// <c>AdScriptValidator.Validate</c> gate an owner-authored brief's own generated script has always
/// had to clear. Nothing about installing a pack ever bypasses, widens, or re-derives that gate — see
/// <c>GenWave.Ads.Tests</c>' own <c>FeatureAdScriptWriterMeetsTheRealValidator</c>/
/// <c>FeatureAdStockKeeping</c> for the real end-to-end generation-path proof, and
/// <c>Story393_AdPackKind</c>'s own integration-level re-pin (this task's own fact).
/// </para>
///
/// <para>
/// <b>ENABLED IS PRESERVE-on-reinstall (RULED at T405 review — corrects this route's own
/// first-cut "always <see langword="true"/>" shape).</b> A brand-new brief this pack has never
/// declared before lands <c>enabled: true</c> (SPEC F162.2's "installed briefs are live by default");
/// a brief this pack ALREADY installed keeps its own <c>enabled</c> exactly as the operator last set
/// it via <c>PATCH /api/ad-briefs/{id}</c> — a reinstall refreshes ONLY
/// <c>premise</c>/<c>tone</c>/<c>structure</c>, never <c>enabled</c>. This route itself carries NO
/// logic for that split at all: <see cref="IAdBriefStore.UpsertAllAsync"/> — and, one level under it,
/// <see cref="IAdBriefStore.UpsertAsync"/>'s own SQL — is where the PRESERVE contract actually lives
/// (see each member's own remarks); this controller just calls it with the manifest's own declared
/// briefs, unmodified.
/// </para>
///
/// <para>
/// <b>AN OWNER BRIEF AND A PACK BRIEF FOR THE SAME BRAND COEXIST, SILENTLY — no note in this
/// response (T405's own "keep simple" ruling).</b> <c>station.ad_brief</c>'s own
/// <c>UNIQUE NULLS NOT DISTINCT (pack_slug, brand)</c> key (db/42) makes <c>(null, "Acme")</c> and
/// <c>("this-pack", "Acme")</c> two DISTINCT rows by construction (T403b's own
/// <c>AnOwnerBriefAndAPackBriefForTheSameBrandAreTwoSeparateRows</c> fact pins this at the
/// constraint level) — installing a pack whose own brief names a brand an operator already
/// owner-authored is therefore never a conflict of any kind, and this response carries nothing
/// further to say about it.
/// </para>
///
/// <para>
/// <b>NO UNINSTALL, NO LISTING ROUTE (a deliberate scope line, not an oversight).</b> Every sibling
/// pack kind (font/avatar/icon) owns a dedicated durable pack row this station can cleanly delete or
/// list by slug; an ad-pack's installed state instead lives INSIDE <c>station.ad_brief</c>, mixed
/// with owner-authored rows — <c>GET /api/ad-briefs</c> (already shipped, T403b) is already this
/// kind's own full-detail listing, and <see cref="IAdBriefStore"/> carries no
/// delete-every-row-for-this-pack-slug member for a DELETE route to call. Neither SPEC F162.2 nor
/// STORY-393's own acceptance criteria ask for either — a future task widens the store if an operator
/// genuinely needs to un-adopt a whole pack's briefs in one action.
/// </para>
/// </summary>
[ApiController]
[Route("api/ad-packs")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class AdPackController(
    CatalogProxyService catalogProxyService,
    CommunityCatalogAccessor catalogAccessor,
    IAdBriefStore briefStore,
    ILogger<AdPackController> logger) : ControllerBase
{
    /// <summary>
    /// POST /api/ad-packs/{slug}/install — see this class's own remarks for the full gate order and
    /// the reasoning behind each one.
    /// </summary>
    [HttpPost("{slug}/install")]
    public async Task<IActionResult> Install(string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length));

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return BadRequest(CatalogInstallShell.BadSlugProblem(slug));

        if (!catalogAccessor.IsEnabled)
            return CatalogInstallShell.DisabledSurfaceResult(Response);

        var (entryError, entryContent) = await CatalogInstallShell.ResolveEntryAsync(
            catalogProxyService, CatalogEntryKind.AdPack, slug, ct);
        if (entryError is not null)
            return entryError;
        if (entryContent is not { } content)
            throw new UnreachableException("CatalogInstallShell.ResolveEntryAsync returned neither an error nor content.");

        // content.ManifestJson is the whole ad-pack document (SPEC F162.2 — no assets[] to fetch; see
        // this class's own NO ASSETS[] TO FETCH remarks) — already hash-verified and size-capped
        // during the read above. CatalogAdPackManifestSerializer's own class remarks carry the
        // brief-count/field-length caps this gate enforces.
        var manifest = CatalogAdPackManifestSerializer.Deserialize(content.ManifestJson);
        if (manifest is null)
            return BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.AdPack, slug));

        // ONE transaction, the whole declared brief list — see this class's own Gate order/ENABLED IS
        // PRESERVE-on-reinstall remarks for the full contract IAdBriefStore.UpsertAllAsync carries.
        var briefs = manifest.Briefs
            .Select(brief => new AdBriefUpsertInput(brief.Brand, brief.Premise, brief.Tone, brief.Structure))
            .ToArray();
        var upserted = await briefStore.UpsertAllAsync(slug, briefs, ct);

        logger.LogInformation(
            "Ad pack installed slug={Slug} briefCount={BriefCount}",
            LogSafeText.Sanitize(slug), upserted.Count);

        return Ok(new AdPackInstallResponse(slug, manifest.PackName, upserted.Select(b => b.Brand).ToArray()));
    }
}
