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
/// <b>Gate order (Install).</b> Route slug format (400) → catalog kill-switch (404, bare) → resolve
/// the entry (<see cref="CatalogInstallShell.ResolveEntryAsync"/>: unknown slug or a non-ad-pack kind
/// ⇒ 404; unreachable ⇒ 503; a withheld manifest/meta ⇒ 502) → parse the manifest
/// (<see cref="CatalogAdPackManifestSerializer.Deserialize"/>: reject ⇒ 400 — that method's own class
/// remarks carry the brief-count/field-length caps this gate enforces, since this is the ONE pack
/// kind whose parsed manifest content becomes a DURABLE write) → ONE
/// <see cref="IAdBriefStore.UpsertAllAsync"/> call, the WHOLE declared brief list, inside ONE
/// transaction (SPEC F162.2 — a reinstall UPDATES every declared brief's content in place, never
/// duplicates; a failure partway through lands NOTHING, never a partially-installed pack) → 200.
/// </para>
///
/// <para>
/// <b>Gate order (Uninstall, SPEC F172.4, STORY-416, PLAN T437).</b> Route slug format (400) →
/// <see cref="IAdBriefStore.UninstallPackAsync"/>, ONE transaction owning the whole thing: refuses
/// first (<see cref="AdPackUninstallResult.InUse"/> ⇒ 409 <see cref="InUseType"/>, naming the owner
/// spots/shows still referencing one of this pack's sponsors — NOTHING written) → else deletes every
/// <c>station.ad_brief</c> row for the slug and retires every <c>source = 'pack'</c>
/// <c>station.ad_spot</c> row for it that was not already <c>retired</c> (nor currently
/// <c>rendering</c> — that state stays undiscardable) →
/// (<see cref="AdPackUninstallResult.NotFound"/> ⇒ 404 — no catalog kill-switch check here, unlike
/// Install: removing something already gone is never gated by whether the catalog is currently
/// reachable).
/// </para>
///
/// <para>
/// <b>204 vs 200.</b> Deleting <c>station.sponsor</c> rows for the slug is NOT unconditional: SPEC F171.5's own
/// rule for deleting a sponsor directly — "no brief, spot (any state), or show references the
/// sponsor" — applies to this uninstall's own sponsor cleanup unexceptioned, and a
/// <c>source = 'pack'</c> spot THIS call just retired (or left <c>rendering</c>) is still, in that
/// rule's own words, a "spot (any state)". <see cref="AdPackUninstallResult.Deleted.KeptSponsors"/>
/// empty ⇒ every pack sponsor deleted cleanly ⇒ 204, bare, exactly STORY-416 AC1/AC3's own shape;
/// non-empty ⇒ at least one pack sponsor survives, orphaned from the now-uninstalled pack ⇒ 200
/// <see cref="AdPackUninstallResponse"/>, naming them — never silently swallowed into a bare success
/// the caller would have no way to notice.
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
/// response (T405's own "keep simple" ruling, still true under PLAN T432's sponsor split).</b>
/// <c>station.sponsor</c>'s own <c>UNIQUE NULLS NOT DISTINCT (pack_slug, name_key)</c> key (db/46)
/// makes an owner sponsor named "Acme" (<c>pack_slug</c> <see langword="null"/>) and this pack's own
/// "Acme" sponsor (<c>pack_slug</c> = <paramref name="slug"/>) two DISTINCT <c>station.sponsor</c>
/// rows by construction — <see cref="ISponsorStore.UpsertPackSponsorsAsync"/> never resolves to an
/// owner-authored sponsor no matter how closely the brand text matches one. Every brief this pack
/// installs therefore names ITS OWN pack-owned sponsor, never an operator's owner-authored one, so
/// installing a pack whose own brief names a brand an operator already owner-authored is never a
/// conflict of any kind, and this response carries nothing further to say about it.
/// </para>
///
/// <para>
/// <b>STILL NO LISTING ROUTE (a deliberate scope line, not an oversight) — but UNINSTALL now exists
/// (PLAN T437, correcting this class's own former "NO UNINSTALL" remarks).</b> Every sibling pack kind
/// (font/avatar/icon/voice) owns a dedicated durable pack row this station can cleanly list by slug; an
/// ad-pack's installed state still lives INSIDE <c>station.ad_brief</c>/<c>station.sponsor</c>, mixed
/// with owner-authored rows — <c>GET /api/ad-briefs</c> (already shipped, T403b) remains this kind's
/// own full-detail listing, and <see cref="IAdBriefStore"/> carries no
/// list-every-pack-slug-installed member for a listing route to call. Uninstall, unlike listing,
/// SPEC F172.4/STORY-416 explicitly ask for: <see cref="IAdBriefStore.UninstallPackAsync"/>
/// (<see cref="Uninstall"/>, below) removes every <c>station.ad_brief</c>/<c>station.sponsor</c> row
/// for a slug and retires its still-live <c>source = 'pack'</c> spots, refusing first when an
/// owner-authored spot or show still names one of that pack's sponsors.
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
    ISponsorStore sponsorStore,
    ILogger<AdPackController> logger) : ControllerBase
{
    /// <summary>The 409 <c>Type</c> shared by <see cref="Uninstall"/>'s own referenced-conflict —
    /// <see cref="JinglePackController"/>'s own <c>InUseType</c> naming precedent, one pack kind
    /// over.</summary>
    const string InUseType = "ad_pack_in_use";

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

        // Every manifest brand resolves to (or creates) a PACK-OWNED sponsor in ONE batch call
        // (replaces this route's own former per-DISTINCT-brand loop, one UpsertPackSponsorAsync
        // round trip per unique brand). UpsertPackSponsorsAsync now owns brand
        // identity itself (fold-collision detection across the WHOLE declared list, one write), so
        // this passes the brands array through unfiltered — no .Distinct() here, the store already
        // folds repeats of the identical brand text onto the same sponsor row, and refuses the WHOLE
        // install if two DIFFERENT brand strings would collide onto one sponsor identity.
        var brands = manifest.Briefs.Select(brief => brief.Brand).ToArray();
        var sponsorResult = await sponsorStore.UpsertPackSponsorsAsync(slug, brands, ct);
        // Early-return gate, matching this method's own "if (...error) return ...;" idiom above —
        // NOT a switch expression assigning `sponsors`, since two of the three outcomes need to
        // return straight out of this action rather than produce a value to keep computing with.
        if (sponsorResult is SponsorPackWriteResult.NamesCollide collide)
            return BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.AdPack, slug,
                $"\"{slug}\"'s manifest declares two brands, \"{LogSafeText.Sanitize(collide.First)}\" " +
                $"and \"{LogSafeText.Sanitize(collide.Second)}\", that resolve to the same sponsor."));
        if (sponsorResult is SponsorPackWriteResult.InvalidField invalid)
            return BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.AdPack, slug,
                $"\"{slug}\"'s manifest names a brand whose {invalid.FieldName} is invalid."));
        if (sponsorResult is not SponsorPackWriteResult.Ok { Sponsors: var sponsors })
            throw new UnreachableException(
                $"ISponsorStore.UpsertPackSponsorsAsync returned {sponsorResult.GetType().Name}, which its own XML doc says never happens.");

        // ONE transaction, the whole declared brief list — see this class's own Gate order/ENABLED IS
        // PRESERVE-on-reinstall remarks for the full contract IAdBriefStore.UpsertAllAsync carries.
        // sponsors is the SAME length, SAME order as brands/manifest.Briefs (UpsertPackSponsorsAsync's
        // own contract) — a straight positional zip, no per-brand dictionary lookup needed.
        var briefs = manifest.Briefs
            .Zip(sponsors, (brief, sponsor) => new AdBriefUpsertInput(sponsor.Id, brief.Premise, brief.Tone, brief.Structure))
            .ToArray();
        var upserted = await briefStore.UpsertAllAsync(slug, briefs, ct);

        logger.LogInformation(
            "Ad pack installed slug={Slug} briefCount={BriefCount}",
            LogSafeText.Sanitize(slug), upserted.Count);

        // POSITIONAL, never a Dictionary keyed on Sponsor.Id: sponsors is guaranteed the SAME
        // length/order as manifest.Briefs (UpsertPackSponsorsAsync's own contract,
        // this method's own "briefs" local above already relies on the identical guarantee), so its
        // own Name at each index IS the brand just upserted for that index — no key collision is even
        // possible this way, whereas ToDictionary(s => s.Id, ...) throws the moment CatalogAdPackManifestSerializer
        // ever again admitted two briefs resolving to the same sponsor id.
        return Ok(new AdPackInstallResponse(
            slug, manifest.PackName, sponsors.Select(s => s.Name).ToArray()));
    }

    /// <summary>
    /// DELETE /api/ad-packs/{slug} — see this class's own remarks for the full gate order (Uninstall)
    /// and the reasoning behind it. Deliberately mirrors <see cref="VoicePackController.Uninstall"/>'s
    /// own switch-based dispatch shape, one pack kind over, substituting
    /// <see cref="AdPackUninstallResult"/>'s three cases for <see cref="VoicePackDeleteResult"/>'s own.
    /// </summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Uninstall(string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length));

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return BadRequest(CatalogInstallShell.BadSlugProblem(slug));

        var result = await briefStore.UninstallPackAsync(slug, ct);
        switch (result)
        {
            // KeptSponsors non-empty: SPEC F171.5's own "spot (any state)" rule kept at least one pack
            // sponsor standing (this class's own 204 vs 200 remarks, PLAN T437 review round 2) — 200,
            // naming the survivors, rather than a bare 204 the caller could never distinguish from a
            // pack that deleted cleanly.
            case AdPackUninstallResult.Deleted { KeptSponsors.Count: > 0 } deleted:
                logger.LogInformation(
                    "Ad pack uninstalled slug={Slug} briefs={Briefs} sponsors={Sponsors} retiredSpots={RetiredSpots} keptSponsors={KeptSponsors}",
                    LogSafeText.Sanitize(slug), deleted.Briefs, deleted.Sponsors, deleted.RetiredSpots, deleted.KeptSponsors.Count);
                return Ok(new AdPackUninstallResponse(
                    slug, deleted.RetiredSpots, deleted.KeptSponsors.Select(SponsorRefDto.From).ToArray()));
            case AdPackUninstallResult.Deleted deleted:
                logger.LogInformation(
                    "Ad pack uninstalled slug={Slug} briefs={Briefs} sponsors={Sponsors} retiredSpots={RetiredSpots}",
                    LogSafeText.Sanitize(slug), deleted.Briefs, deleted.Sponsors, deleted.RetiredSpots);
                return NoContent();
            case AdPackUninstallResult.NotFound:
                return NotFound(CatalogInstallShell.UnknownInstalledPackProblem(CatalogEntryKind.AdPack, slug));
            case AdPackUninstallResult.InUse inUse:
                logger.LogWarning(
                    "Ad pack uninstall refused slug={Slug} spotCount={SpotCount} showCount={ShowCount}",
                    LogSafeText.Sanitize(slug), inUse.SpotTitles.Count, inUse.ShowNames.Count);
                return Conflict(InUseProblem(slug, inUse.SpotTitles, inUse.ShowNames));
            default:
                throw new UnreachableException($"Unhandled {nameof(AdPackUninstallResult)} case.");
        }
    }

    /// <summary>The <see cref="Uninstall"/>-only 409 body — <see cref="VoicePackController"/>'s own
    /// <c>BuildReferencedDetail</c> "join whichever parts are non-empty" idiom, one pack kind over.
    /// <see cref="ProblemDetails.Detail"/> is the whole body (drops this method's former
    /// <see cref="ProblemDetails.Extensions"/> entries: SPEC F172.4 and STORY-416 AC2
    /// ask only for the prose <c>Detail</c>, no sibling pack controller's own 409 emits raw
    /// <c>Extensions</c> arrays, and <see cref="VoicePackController"/>'s own <c>BuildReferencedDetail</c>
    /// this method's summary claims to mirror never carried them either).</summary>
    static ProblemDetails InUseProblem(string slug, IReadOnlyList<string> spotTitles, IReadOnlyList<string> showNames) =>
        new()
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Ad pack is referenced.",
            Type = InUseType,
            Detail = BuildInUseDetail(slug, spotTitles, showNames),
        };

    static string BuildInUseDetail(string slug, IReadOnlyList<string> spotTitles, IReadOnlyList<string> showNames)
    {
        var parts = new List<string>();
        if (spotTitles.Count > 0)
            parts.Add($"ad spot(s) {string.Join(", ", spotTitles.Select(title => $"\"{LogSafeText.Sanitize(title)}\""))}");

        if (showNames.Count > 0)
            parts.Add($"show(s) {string.Join(", ", showNames.Select(name => $"\"{LogSafeText.Sanitize(name)}\""))}");

        return parts.Count > 0
            ? $"\"{slug}\" is still referenced by {string.Join(" and ", parts)} and cannot be uninstalled — remove or reassign those first."
            : $"\"{slug}\" is still referenced and cannot be uninstalled.";
    }
}
