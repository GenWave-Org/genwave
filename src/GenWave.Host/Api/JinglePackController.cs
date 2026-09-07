using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Catalog;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/jingle-packs/{slug}/install</c> and <c>DELETE /api/jingle-packs/{slug}</c> (SPEC
/// F165.1-F165.6, STORY-399/401, PLAN T414) — installs/removes a Dean-curated jingle pack (plain
/// name: "background music" pack — gh-#707, "jingle pack" is this app's own closed-set catalog kind
/// name, not radio jargon in a user-facing string) from the Community Catalog's <c>jingle-pack</c>
/// kind into <c>library.media</c>, at <see cref="PacksOptions.JingleRoot"/>. F79 shell, mirrors
/// <see cref="VoicePackController"/>'s own shape one asset kind over: the same
/// <see cref="AdminSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Settings"/> pairing, the
/// same catalog-slug vocabulary, the same no-oracle <see cref="ProblemDetails"/> idioms (F15.7).
///
/// <para>
/// <b>Every asset IS a library row (unlike a voice pack's own <c>.pt</c> files) — install therefore
/// enriches, not just fetches.</b> A jingle asset is a real, playable <c>library.media</c> row
/// (imaging kind <c>jingle</c>): <see cref="ILoudnessAnalyzer"/>/<see cref="ICueAnalyzer"/> run over
/// each STAGED file the same way library scan does, plus a TagLib read for the technical properties
/// (duration/sample rate/channels/bitrate — mirrors <c>Enrich.Enricher.ReadTags</c>'s own extraction
/// exactly). <b>Unlike that scanner's own tolerant "store what we can, NULL the rest" posture, a
/// jingle install fails the WHOLE pack the instant any one asset fails to decode or measure</b> (SPEC
/// F165.5 "no half-installed pack") — a bad file from a compromised or malformed catalog origin never
/// lands a row with a null cue the ad worker's bed-pool query would then have to defend against.
/// </para>
///
/// <para>
/// <b>Own per-asset fetch loop, uncached (SPEC F165.1, PLAN T414).</b> Unlike
/// <see cref="VoicePackController"/>'s own <see cref="CatalogInstallShell.FetchAllAssetsAsync"/> call,
/// this route fetches through <see cref="CatalogInstallShell.FetchAllAssetsUncachedAsync"/> — a jingle
/// asset can run up to 5 MiB, and this admin-only surface's own bounded asset cache
/// (<c>CatalogProxyService.MaxCachedAssets</c>) was never sized to hold assets that large (see that
/// method's own remarks).
/// </para>
///
/// <para>
/// <b>Gate order (Install).</b> Route slug format (400) → catalog kill-switch (404, bare) → resolve
/// the entry (<see cref="CatalogInstallShell.ResolveEntryAsync"/>) → parse the manifest
/// (<see cref="CatalogJinglePackManifestSerializer.Deserialize"/>: reject ⇒ 400 malformed, role is
/// already closed-set validated there) → fetch every declared asset, uncached
/// (<see cref="CatalogInstallShell.FetchAllAssetsUncachedAsync"/>) → cross-check the manifest's own
/// declared files against what was actually fetched → resolve the ads library id
/// (<see cref="ILibraryRepository.GetByNameAsync"/>, <see cref="AdsOptions.LibraryName"/> — every
/// jingle row lands in the SAME library the ad worker's own bed-pool query reads) → STAGE + ENRICH
/// every asset into a per-attempt scratch directory (loudness/readability/duration hard-fail the
/// whole install on any miss; a null cue analysis means no silence was found, never a refusal —
/// <see cref="JingleCuePolicy.Apply"/> decides — <see cref="StageAndEnrichAllAsync"/>) → apply the
/// role-based cue policy
/// (<see cref="JingleCuePolicy.Apply"/>, SPEC F165.3) → MOVE every staged file into its final,
/// per-slug home under <see cref="PacksOptions.JingleRoot"/> (a PRE-EXISTING target — a previous
/// install's own live file — is displaced aside first, never overwritten directly, the same
/// copy-then-atomic-rename-over discipline <see cref="VoicePackController.WriteVoiceFilesAsync"/>'s
/// own remarks describe in full; ANY failure restores every displaced file and deletes every NEW file
/// this attempt wrote) → ONE <see cref="IJinglePackStore.UpsertAsync"/> call (a reinstall-drops-a-
/// referenced-title race unwinds identically) → on success, every displaced file is deleted (the
/// previous install is now genuinely superseded), any title a reinstall DROPPED from the manifest
/// has its now-orphaned file unlinked too (<see cref="IJinglePackStore.FindPathsForPackAsync"/>'s
/// own remarks — its row was already dropped by <see cref="IJinglePackStore.UpsertAsync"/>), and the
/// scratch directory is removed → 200.
/// </para>
///
/// <para>
/// <b>DELETE is a guard-READ then two single-schema deletes, not one in-statement guard (SPEC
/// F165.6, STORY-401).</b> Unlike <see cref="VoicePackController"/>'s own single guarded DELETE
/// statement, <see cref="IJinglePackStore.DeleteAsync"/>'s own remarks explain why a jingle pack
/// cannot follow that shape (the db/22 cross-schema role boundary — <c>library.media</c> rows behind
/// <c>library_svc</c>, the <c>station.ad_spot.bed_media_id</c> reference behind <c>station_svc</c>).
/// A clean delete unlinks every path the store names, then removes the pack's own slug folder if that
/// leaves it empty (<see cref="JinglePackDeleteResult.Deleted"/>'s own remarks) before returning 204.
/// </para>
/// </summary>
[ApiController]
[Route("api/jingle-packs")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class JinglePackController(
    CatalogProxyService catalogProxyService,
    CommunityCatalogAccessor catalogAccessor,
    IJinglePackStore jinglePackStore,
    ILibraryRepository libraryRepository,
    ILoudnessAnalyzer loudnessAnalyzer,
    ICueAnalyzer cueAnalyzer,
    IEnergyAnalyzer energyAnalyzer,
    IOptions<PacksOptions> packsOptions,
    IOptions<AdsOptions> adsOptions,
    ILogger<JinglePackController> logger) : ControllerBase
{
    /// <summary>App-side ceiling on the RUNNING total across every asset one jingle-pack entry
    /// declares. Own judgment call, this controller's own DoS bound — SPEC F165.1 pins only the
    /// PER-ASSET ceiling (<see cref="PacksOptions.JingleAssetMaxBytes"/>, 5 MiB default) and says
    /// nothing about a whole-pack total; a realistic pack is a handful of background-music beds, a
    /// sting or two, and one station id, nowhere near the theoretical 32-assets × 5-MiB-each = 160 MiB
    /// worst case <see cref="CatalogJinglePackManifestSerializer.MaxAssetsPerPack"/> alone would allow.
    /// Mirrors <see cref="VoicePackController"/>'s own per-controller-owned <c>MaxPackBytes</c>
    /// precedent — <see cref="CatalogInstallShell"/> only ever receives this as a parameter, never
    /// owns a whole-pack ceiling itself.</summary>
    const long MaxPackBytes = 64 * 1024 * 1024;

    /// <summary>Rendered verbatim into <see cref="CatalogInstallShell.PackTooLargeProblem"/>'s own
    /// Detail text. Deliberately NOT a SPEC section number (T414 review round 2, "Rulings that
    /// stand") — <see cref="MaxPackBytes"/> is this controller's own invented DoS bound, not
    /// something F165.1 or any other SPEC clause actually mandates; citing F165.1 here would
    /// misattribute an app-side judgment call as a spec requirement.</summary>
    const string MaxPackBytesSpecRef = "this controller's own pack-size ceiling, not a SPEC-mandated bound";

    /// <summary>SPEC reference for a single asset's own decode/enrichment hard-fail (F165.5's
    /// "no half-installed pack" posture) — distinct from <see cref="MaxPackBytesSpecRef"/>'s transport
    /// ceiling.</summary>
    const string EnrichmentFailedSpecRef = "F165.5";

    // ProblemDetails.Type tokens (T414 review round 2 finding F8, T413's VoicePackController own
    // NotSupportedEngineType precedent) — one per distinct failure SHAPE a client might branch on,
    // not one per factory method (InstallRefusedProblem/UninstallRefusedProblem share InUseType;
    // AdsLibraryMissingProblem/JingleFileWriteFailedProblem/JinglePackInstallFailedProblem share
    // InstallFailedType — all three are "the install failed server-side for a reason the client
    // cannot act on differently").
    const string NotFoundType = "jingle_pack_not_found";
    const string InUseType = "jingle_pack_in_use";
    const string AssetUnreadableType = "jingle_asset_unreadable";
    const string InstallFailedType = "jingle_pack_install_failed";

    /// <summary>
    /// POST /api/jingle-packs/{slug}/install — see this class's own remarks for the full gate order
    /// and the reasoning behind each step. Reads top-down: gate
    /// (<see cref="ResolveGatedManifestAsync"/>) → fetch → cross-check → resolve the ads library →
    /// stage+enrich (own scratch directory, cleaned up in every exit path via <c>finally</c>) → move
    /// into place → one DB call → response.
    /// </summary>
    [HttpPost("{slug}/install")]
    public async Task<IActionResult> Install(string slug, CancellationToken ct)
    {
        var (gateError, contentOrNull, manifestOrNull) = await ResolveGatedManifestAsync(slug, ct);
        if (gateError is not null)
            return gateError;
        if (contentOrNull is not { } content || manifestOrNull is not { } manifest)
            throw new UnreachableException("ResolveGatedManifestAsync returned neither an error nor a resolved manifest.");

        var (assetsError, fetchedAssetsOrNull) = await CatalogInstallShell.FetchAllAssetsUncachedAsync(
            catalogProxyService, slug, content,
            new CatalogInstallShell.PackFetchPolicy(
                CatalogEntryKind.JinglePack, packsOptions.Value.JingleAssetMaxBytes, MaxPackBytes, MaxPackBytesSpecRef),
            ct);
        if (assetsError is not null)
            return assetsError;
        if (fetchedAssetsOrNull is not { } fetchedAssets)
            throw new UnreachableException("CatalogInstallShell.FetchAllAssetsUncachedAsync returned neither an error nor a fetched-asset map.");

        var crossCheckError = CrossCheckManifestAssets(slug, manifest, fetchedAssets);
        if (crossCheckError is not null)
            return crossCheckError;

        var library = await libraryRepository.GetByNameAsync(adsOptions.Value.LibraryName, ct);
        if (library is not { } resolvedLibrary)
        {
            logger.LogError(
                "Jingle pack install failed: the ads library \"{LibraryName}\" does not exist slug={Slug}",
                LogSafeText.Sanitize(adsOptions.Value.LibraryName), LogSafeText.Sanitize(slug));
            return StatusCode(StatusCodes.Status500InternalServerError, AdsLibraryMissingProblem(slug));
        }

        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packsOptions.Value.JingleRoot));
        var packDir = Path.Combine(canonicalRoot, slug);
        var stagingDir = Path.Combine(canonicalRoot, $"{slug}.staging-{Guid.NewGuid():N}");

        try
        {
            var (stageError, stagedOrNull) = await StageAndEnrichAllAsync(slug, stagingDir, manifest, fetchedAssets, ct);
            if (stageError is not null)
                return stageError;
            if (stagedOrNull is not { } staged)
                throw new UnreachableException("StageAndEnrichAllAsync returned neither an error nor a staged-asset list.");

            var (moveError, writtenOrNull) = await MoveStagedFilesIntoPlaceAsync(slug, packDir, canonicalRoot, staged, ct);
            if (moveError is not null)
                return moveError;
            if (writtenOrNull is not { } written)
                throw new UnreachableException("MoveStagedFilesIntoPlaceAsync returned neither an error nor a written-file list.");

            var assetInputs = BuildAssetInputs(manifest.PackName, resolvedLibrary.Id, staged, written);

            // Read BEFORE the upsert (mirrors IJinglePackStore.FindPathsForPackAsync's own remarks):
            // a reinstall that drops a title from the manifest already drops that title's OWN row
            // (UpsertAsync's own job), but its file survives on disk until something diffs the
            // previous install's own paths against what this attempt actually wrote and unlinks the
            // difference — that diff happens after a successful upsert, below.
            var previousPaths = await jinglePackStore.FindPathsForPackAsync(slug, ct);

            JinglePackUpsertResult upsertResult;
            try
            {
                upsertResult = await jinglePackStore.UpsertAsync(slug, content.ManifestJson, slug, assetInputs, ct);
            }
            catch (Exception ex)
            {
                // ANY store failure after the moves above — not just the referenced-title race the
                // switch below maps to a 409 — must unwind what this attempt just wrote AND restore
                // whatever it displaced to make room for it (mirrors VoicePackController.Install's
                // own upsert-catch remarks), and that includes a client-disconnect/command-
                // cancellation surfacing as OperationCanceledException: an unwind that only ran for
                // "everything except a cancel" would leave exactly the orphaned-file-with-no-DB-row
                // state this whole method exists to prevent.
                UnwindWritten(written);
                if (ex is OperationCanceledException)
                    throw;

                logger.LogError(ex, "Jingle pack install failed while upserting the store slug={Slug}", LogSafeText.Sanitize(slug));
                return StatusCode(StatusCodes.Status500InternalServerError, JinglePackInstallFailedProblem(slug));
            }

            IReadOnlyList<long> mediaIds;
            switch (upsertResult)
            {
                case JinglePackUpsertResult.Upserted upserted:
                    mediaIds = upserted.MediaIds;
                    break;
                case JinglePackUpsertResult.Refused refused:
                    // A concurrent write, or an ad spot inserted between the store's own guard-read
                    // and this request's write, won the race — the files this attempt just wrote
                    // never get to keep a home, and anything it displaced to make room for them comes
                    // straight back (F165.5's all-or-nothing posture applies across the write-then-DB
                    // boundary too).
                    UnwindWritten(written);
                    logger.LogWarning(
                        "Jingle pack install refused slug={Slug} adSpotCount={AdSpotCount}",
                        LogSafeText.Sanitize(slug), refused.AdSpotIds.Count);
                    return Conflict(InstallRefusedProblem(slug, refused.AdSpotIds));
                default:
                    throw new UnreachableException($"Unhandled {nameof(JinglePackUpsertResult)} case.");
            }

            // SUCCESS — the DB write has committed, so any file this attempt displaced to make room
            // for a re-install's own overwrite is now genuinely superseded: delete it, after the
            // commit. A crash exactly here leaves an orphaned `.prev-*` sibling on disk, never a torn
            // install — the next re-install of the same slug displaces it again like any other
            // pre-existing target, and uninstall only ever unlinks the paths the DB row itself carries.
            foreach (var file in written)
            {
                if (file.DisplacedPath is { } displacedPath)
                    TryDelete(displacedPath);
            }

            // A dropped-title reinstall's own orphan: previousPaths named it (it was installed before
            // this attempt), but nothing in `written` names it (this attempt's own manifest no longer
            // declares it, so UpsertAsync already dropped its row) — its file would otherwise survive
            // on disk forever, since uninstall only ever unlinks the paths the DB row itself carries.
            var keptPaths = written.Select(w => w.Path).ToHashSet(StringComparer.Ordinal);
            var orphanedPaths = previousPaths.Where(path => !keptPaths.Contains(path)).ToArray();
            if (orphanedPaths.Length > 0)
                UnlinkPackAssetFiles(slug, orphanedPaths);

            logger.LogInformation(
                "Jingle pack installed slug={Slug} packName={PackName} assetCount={AssetCount}",
                LogSafeText.Sanitize(slug), LogSafeText.Sanitize(manifest.PackName), staged.Count);

            var assetsResponse = staged
                .Zip(mediaIds, (asset, mediaId) => new JinglePackInstalledAssetResponse(asset.Asset.File, asset.Asset.Role, mediaId))
                .ToArray();

            return Ok(new JinglePackInstallResponse(slug, manifest.PackName, assetsResponse));
        }
        finally
        {
            // Best-effort — on success every staged file was MOVED out already, so this ordinarily
            // removes an empty directory; on any failure it discards whatever staging left behind.
            // Never the pack's own live install: `stagingDir` is a `.staging-<guid>` SIBLING of
            // `packDir`, a distinct path this attempt alone created.
            TryRemoveDirectory(stagingDir);
        }
    }

    /// <summary>
    /// Route slug format (400) → catalog kill-switch (404, bare) → resolve the catalog entry
    /// (<see cref="CatalogInstallShell.ResolveEntryAsync"/>) → parse the manifest
    /// (<see cref="CatalogJinglePackManifestSerializer.Deserialize"/>: reject ⇒ 400 malformed).
    /// Extracted out of <see cref="Install"/> (mirrors <see cref="VoicePackController.ResolveGatedManifestAsync"/>'s
    /// own reasoning) so that method itself reads as a single top-down pipeline.
    /// </summary>
    async Task<(IActionResult? Error, CatalogEntryContent? Content, CatalogJinglePackManifest? Manifest)> ResolveGatedManifestAsync(
        string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return (BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length)), null, null);

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return (BadRequest(CatalogInstallShell.BadSlugProblem(slug)), null, null);

        if (!catalogAccessor.IsEnabled)
            return (CatalogInstallShell.DisabledSurfaceResult(Response), null, null);

        var (entryError, entryContent) = await CatalogInstallShell.ResolveEntryAsync(
            catalogProxyService, CatalogEntryKind.JinglePack, slug, ct);
        if (entryError is not null)
            return (entryError, null, null);
        if (entryContent is not { } content)
            throw new UnreachableException("CatalogInstallShell.ResolveEntryAsync returned neither an error nor content.");

        var manifest = CatalogJinglePackManifestSerializer.Deserialize(content.ManifestJson);
        if (manifest is null)
            return (BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.JinglePack, slug)), null, null);

        return (null, content, manifest);
    }

    /// <summary>
    /// DELETE /api/jingle-packs/{slug} — see this class's own remarks for the full contract
    /// <see cref="IJinglePackStore.DeleteAsync"/> enforces.
    /// </summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Uninstall(string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length));

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return BadRequest(CatalogInstallShell.BadSlugProblem(slug));

        var result = await jinglePackStore.DeleteAsync(slug, ct);
        switch (result)
        {
            case JinglePackDeleteResult.Deleted deleted:
                UnlinkPackAssetFiles(slug, deleted.Paths);
                RemovePackFolderIfEmpty(slug);
                logger.LogInformation(
                    "Jingle pack uninstalled slug={Slug} fileCount={FileCount}", LogSafeText.Sanitize(slug), deleted.Paths.Count);
                return NoContent();
            case JinglePackDeleteResult.NotFound:
                return NotFound(JinglePackNotFoundProblem(slug));
            case JinglePackDeleteResult.Refused refused:
                logger.LogWarning(
                    "Jingle pack uninstall refused slug={Slug} adSpotCount={AdSpotCount}",
                    LogSafeText.Sanitize(slug), refused.AdSpotIds.Count);
                return Conflict(UninstallRefusedProblem(slug, refused.AdSpotIds));
            default:
                throw new UnreachableException($"Unhandled {nameof(JinglePackDeleteResult)} case.");
        }
    }

    /// <summary>Confirms every asset <paramref name="manifest"/> declares was actually fetched — a
    /// manifest naming a file the catalog index itself never listed (or that failed its own fetch for
    /// some other already-handled reason) is refused here rather than reaching staging with a missing
    /// dictionary entry.</summary>
    static IActionResult? CrossCheckManifestAssets(
        string slug, CatalogJinglePackManifest manifest, Dictionary<string, CatalogInstallShell.CatalogFetchedAsset> fetchedAssets)
    {
        foreach (var asset in manifest.Assets)
        {
            if (!fetchedAssets.ContainsKey(asset.File))
                return new BadRequestObjectResult(CatalogInstallShell.UndeclaredManifestAssetProblem(CatalogEntryKind.JinglePack, slug, asset.File));
        }

        return null;
    }

    /// <summary>
    /// Writes every fetched asset's bytes to <paramref name="stagingDir"/> under its OWN declared
    /// file name (never a temp-suffixed name — TagLib's own <see cref="TagLib.File.Create(string)"/>
    /// resolves a file's format from its extension, so a real <c>.wav</c>/<c>.mp3</c>/<c>.flac</c>
    /// name must survive through every read below), then runs it through the SAME
    /// <see cref="ILoudnessAnalyzer"/>/<see cref="ICueAnalyzer"/> analyzers an ordinary library scan
    /// already uses, plus one TagLib read for its technical properties (mirrors
    /// <c>Enrich.Enricher.ReadTags</c>'s own extraction exactly). <see cref="JingleCuePolicy.Apply"/>
    /// (SPEC F165.3) then decides what actually gets stored for this asset's own role.
    ///
    /// <para>
    /// <b>Hard-fails the WHOLE install the instant any one asset fails to decode or measure</b> (SPEC
    /// F165.5) — unlike <c>Enrich.Enricher</c>'s own tolerant "store what we can, NULL the rest"
    /// posture for an ordinary scanned file. A jingle asset with an unmeasurable loudness or an
    /// unreadable/zero-length duration never becomes a <c>library.media</c> row at all: the caller
    /// (<see cref="Install"/>) returns 422 (<c>Type = "jingle_asset_unreadable"</c>) naming the
    /// offending file, and nothing this attempt already staged is ever moved into
    /// <see cref="PacksOptions.JingleRoot"/>. A null <see cref="ICueAnalyzer"/> result is
    /// NOT one of these failures — it means no silence was found (full-file playback intended), and
    /// <see cref="JingleCuePolicy.Apply"/> alone decides what that means for this asset's own role.
    /// </para>
    ///
    /// <para>
    /// The count check just before this method returns is the STRUCTURAL form of F165.5 — the
    /// per-asset returns above are the diagnosis (which file, which gate), the count is the
    /// guarantee (every declared asset staged, or none did).
    /// </para>
    /// </summary>
    async Task<(IActionResult? Error, IReadOnlyList<StagedJingleAsset>? Staged)> StageAndEnrichAllAsync(
        string slug, string stagingDir, CatalogJinglePackManifest manifest,
        Dictionary<string, CatalogInstallShell.CatalogFetchedAsset> fetchedAssets, CancellationToken ct)
    {
        Directory.CreateDirectory(stagingDir);
        var staged = new List<StagedJingleAsset>(manifest.Assets.Count);

        try
        {
            foreach (var asset in manifest.Assets)
            {
                var stagedPath = Path.Combine(stagingDir, asset.File);
                await System.IO.File.WriteAllBytesAsync(stagedPath, fetchedAssets[asset.File].Bytes, ct);

                var loudness = await loudnessAnalyzer.AnalyzeAsync(stagedPath, ct);
                if (!loudness.Measurable)
                    return (UnprocessableEntity(EnrichmentFailedProblem(slug, asset.File)), null);

                var analyzedCue = await cueAnalyzer.AnalyzeAsync(stagedPath, ct);

                TagLib.Properties? props;
                try
                {
                    using var tagFile = TagLib.File.Create(stagedPath);
                    props = tagFile.Properties;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex, "Jingle pack install could not read technical properties slug={Slug} file={File}",
                        LogSafeText.Sanitize(slug), LogSafeText.Sanitize(asset.File));
                    return (UnprocessableEntity(EnrichmentFailedProblem(slug, asset.File)), null);
                }

                if (props is not { Duration.TotalSeconds: > 0 })
                    return (UnprocessableEntity(EnrichmentFailedProblem(slug, asset.File)), null);

                var durationSec = props.Duration.TotalSeconds;
                var storedCue = JingleCuePolicy.Apply(asset.Role, analyzedCue, durationSec);
                var energy = await energyAnalyzer.AnalyzeAsync(stagedPath, storedCue.CueInSec, storedCue.CueOutSec, ct);

                staged.Add(new StagedJingleAsset(
                    asset, stagedPath, loudness, storedCue, energy,
                    (int)props.Duration.TotalMilliseconds,
                    props.AudioSampleRate > 0 ? props.AudioSampleRate : null,
                    props.AudioChannels > 0 ? (short)props.AudioChannels : null,
                    props.AudioBitrate > 0 ? props.AudioBitrate : null));
            }

            if (staged.Count != manifest.Assets.Count)
            {
                var missingFile = manifest.Assets.First(a => staged.All(s => s.Asset.File != a.File)).File;
                logger.LogError(
                    "Jingle pack install staged {Staged} of {Declared} assets slug={Slug}",
                    staged.Count, manifest.Assets.Count, LogSafeText.Sanitize(slug));
                return (UnprocessableEntity(EnrichmentFailedProblem(slug, missingFile)), null);
            }

            return (null, staged);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Jingle pack install failed while staging asset files slug={Slug}", LogSafeText.Sanitize(slug));
            return (StatusCode(StatusCodes.Status500InternalServerError, JingleFileWriteFailedProblem(slug)), null);
        }
    }

    /// <summary>
    /// Moves every staged file into <paramref name="packDir"/> under its own file name — a
    /// separator-aware canonical-root re-assertion on every composed target guards the write even
    /// though <c>asset.File</c> can only ever match <c>CatalogJinglePackManifestSerializer.FileFormat</c>'s
    /// own closed pattern by construction (no <c>/</c> or <c>..</c> possible).
    ///
    /// <para>
    /// <b>A pre-existing target is COPIED aside, never overwritten blind</b> — mirrors
    /// <see cref="VoicePackController.WriteVoiceFilesAsync"/>'s own T413-earned discipline exactly,
    /// one asset kind over: a re-install of an already-installed slug targets the SAME path a
    /// previous, still-live install wrote, so a pre-existing target is first COPIED (not moved) to an
    /// unpredictable sibling name in the SAME directory (<c>&lt;target&gt;.prev-&lt;guid&gt;</c> —
    /// <see cref="WrittenJingleFile.DisplacedPath"/> records it), and only then is the staged file
    /// MOVED over it (<c>overwrite: true</c>) — one atomic rename-over, so <c>target</c>'s own name is
    /// never briefly absent. Moving the ALREADY-STAGED file itself (rather than writing a fresh
    /// temp-named copy first, the way <see cref="VoicePackController.WriteVoiceFilesAsync"/> must,
    /// since it writes bytes directly rather than staging first) needs no separate temp name at all
    /// here: <paramref name="staged"/>'s own <see cref="StagedJingleAsset.StagedPath"/> already lives
    /// outside <paramref name="packDir"/>, so the move itself IS the atomic rename (same filesystem —
    /// <c>stagingDir</c> and <paramref name="packDir"/> are both direct children of
    /// <see cref="PacksOptions.JingleRoot"/>).
    /// </para>
    ///
    /// <para>
    /// ANY failure (this method's own catch, or either post-move exit in <see cref="Install"/>)
    /// unwinds through <see cref="UnwindWritten"/> — restores every displaced file, deletes every new
    /// one this attempt itself placed. A cancellation surfacing as <see cref="OperationCanceledException"/>
    /// unwinds identically, then propagates rather than answering with a 500 (mirrors
    /// <see cref="VoicePackController.WriteVoiceFilesAsync"/>'s own remarks on this exact point).
    /// </para>
    /// </summary>
    async Task<(IActionResult? Error, IReadOnlyList<WrittenJingleFile>? Written)> MoveStagedFilesIntoPlaceAsync(
        string slug, string packDir, string canonicalRoot, IReadOnlyList<StagedJingleAsset> staged, CancellationToken ct)
    {
        Directory.CreateDirectory(packDir);
        var written = new List<WrittenJingleFile>(staged.Count);
        // The CURRENT asset's own move, once it has displaced a previous install's file aside but
        // before the rename-over that would fold it into `written` has completed — mirrors
        // VoicePackController.WriteVoiceFilesAsync's own `pending` slot exactly.
        WrittenJingleFile? pending = null;

        try
        {
            foreach (var asset in staged)
            {
                ct.ThrowIfCancellationRequested();

                var target = Path.GetFullPath(Path.Combine(packDir, asset.Asset.File));
                if (!IsUnderCanonicalRoot(target, canonicalRoot))
                    throw new UnreachableException($"Jingle asset target escaped the jingle root for file \"{asset.Asset.File}\".");

                if (System.IO.File.Exists(target))
                {
                    var displacedPath = $"{target}.prev-{Guid.NewGuid():N}";
                    System.IO.File.Copy(target, displacedPath);
                    pending = new WrittenJingleFile(asset.Asset.File, target, displacedPath);
                }

                System.IO.File.Move(asset.StagedPath, target, overwrite: true);
                written.Add(pending ?? new WrittenJingleFile(asset.Asset.File, target, null));
                pending = null;
            }

            return (null, written);
        }
        catch (Exception ex)
        {
            UnwindWritten(pending is { } current ? [.. written, current] : written);

            if (ex is OperationCanceledException)
                throw;

            logger.LogError(ex, "Jingle pack install failed while moving asset files into place slug={Slug}", LogSafeText.Sanitize(slug));
            return (StatusCode(StatusCodes.Status500InternalServerError, JingleFileWriteFailedProblem(slug)), null);
        }
    }

    /// <summary>Builds the <c>library.media</c> insert for every staged asset, keyed positionally
    /// against <paramref name="written"/> (<see cref="MoveStagedFilesIntoPlaceAsync"/> produces both
    /// lists by iterating the SAME <paramref name="staged"/> sequence, so index <c>i</c> in one is
    /// always index <c>i</c>'s own final path in the other) — stats the FINAL, post-move file for
    /// <see cref="AuthoredMediaInsert.SizeBytes"/>/<see cref="AuthoredMediaInsert.Mtime"/>, mirrors
    /// <c>AdRenderService.BuildInsert</c>'s own "stat what actually landed" precedent.</summary>
    static IReadOnlyList<JinglePackAssetInput> BuildAssetInputs(
        string manifestPackName, long libraryId, IReadOnlyList<StagedJingleAsset> staged, IReadOnlyList<WrittenJingleFile> written)
    {
        var inputs = new List<JinglePackAssetInput>(staged.Count);
        for (var i = 0; i < staged.Count; i++)
        {
            var asset = staged[i];
            var finalPath = written[i].Path;
            var info = new FileInfo(finalPath);
            var media = new AuthoredMediaInsert(
                Path: finalPath,
                Format: Path.GetExtension(asset.Asset.File).TrimStart('.'),
                LibraryId: libraryId,
                SizeBytes: info.Length,
                Mtime: info.LastWriteTimeUtc,
                Tags: new AudioTags(manifestPackName, asset.Asset.Title),
                Loudness: asset.Loudness,
                Cue: asset.Cue,
                Energy: asset.Energy,
                DurationMs: asset.DurationMs,
                SampleRate: asset.SampleRate,
                Channels: asset.Channels,
                BitrateKbps: asset.BitrateKbps,
                Kind: ImagingKind.Jingle);

            inputs.Add(new JinglePackAssetInput(media, asset.Asset.Role));
        }

        return inputs;
    }

    /// <summary>Unlinks every path named, TWO callers: a successful <see cref="IJinglePackStore.DeleteAsync"/>'s
    /// own full asset list (read BEFORE either delete ran — see that member's own remarks), or
    /// <see cref="Install"/>'s own reinstall-time orphan set (a title <see cref="IJinglePackStore.FindPathsForPackAsync"/>
    /// named as previously installed, but this attempt's own manifest no longer declares — already
    /// dropped its row by <see cref="IJinglePackStore.UpsertAsync"/>, its file left to this method to
    /// finish). A missing file is a no-op (idempotent by construction — <see cref="TryDelete"/>
    /// swallows the "already gone" case); a path this station's own root check rejects is logged and
    /// skipped rather than ever escaping <see cref="PacksOptions.JingleRoot"/>.</summary>
    void UnlinkPackAssetFiles(string slug, IReadOnlyList<string> paths)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packsOptions.Value.JingleRoot));
        foreach (var path in paths)
        {
            var target = Path.GetFullPath(path);
            if (!IsUnderCanonicalRoot(target, canonicalRoot))
            {
                logger.LogWarning(
                    "Jingle pack install/uninstall skipped a file outside the jingle root slug={Slug} path={Path}",
                    LogSafeText.Sanitize(slug), LogSafeText.Sanitize(path));
                continue;
            }

            TryDelete(target);
        }
    }

    /// <summary>Removes the pack's own slug folder once its asset files are unlinked, but ONLY if
    /// that leaves it empty (<see cref="JinglePackDeleteResult.Deleted"/>'s own remarks) — a
    /// not-found or not-empty directory is left exactly as-is, never forced: nothing but another
    /// install/uninstall of this same slug ever writes into it, so a non-empty leftover self-heals on
    /// the next one.</summary>
    void RemovePackFolderIfEmpty(string slug)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packsOptions.Value.JingleRoot));
        var packDir = Path.Combine(canonicalRoot, slug);
        try
        {
            Directory.Delete(packDir);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            _ = ex; // already gone, or not empty — both fine, see this method's own remarks.
        }
    }

    /// <summary>Best-effort cleanup of this attempt's OWN scratch staging directory — never the
    /// pack's own live install folder (a distinct, `.staging-`-suffixed sibling path this attempt
    /// alone created).</summary>
    void TryRemoveDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Jingle pack install could not remove its own staging directory {Path}", LogSafeText.Sanitize(path));
        }
    }

    /// <summary>Unwinds every file THIS install attempt itself moved into place (<see cref="MoveStagedFilesIntoPlaceAsync"/>'s
    /// own return value, mirrors <see cref="VoicePackController.UnwindWritten"/> exactly): when this
    /// attempt displaced a PREVIOUS install's own live file to make room for its own, the displaced
    /// COPY is renamed straight back OVER the new one FIRST — an atomic rename-over, so the target
    /// name is never briefly absent on the unwind path either. Only when nothing was displaced does
    /// the new file simply get deleted. Either way a failed re-install leaves the previous install's
    /// own bytes, and its still-surviving DB row, in exactly the state they were in before this
    /// attempt ever started.</summary>
    void UnwindWritten(IReadOnlyList<WrittenJingleFile> written)
    {
        foreach (var file in written)
        {
            if (file.DisplacedPath is { } displacedPath)
                TryRestore(displacedPath, file.Path);
            else
                TryDelete(file.Path);
        }
    }

    void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Jingle pack install/uninstall could not remove file {Path}", LogSafeText.Sanitize(path));
        }
    }

    void TryRestore(string displacedPath, string target)
    {
        try
        {
            System.IO.File.Move(displacedPath, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                ex, "Jingle pack install could not restore the previous file at {Path} from its displaced copy at {DisplacedPath}",
                LogSafeText.Sanitize(target), LogSafeText.Sanitize(displacedPath));
        }
    }

    /// <summary>Separator-aware canonical-root check — mirrors <see cref="VoicePackController.IsUnderCanonicalRoot"/>'s
    /// own idiom, reimplemented here rather than shared across controllers (each one owns its own
    /// small copy, the same "keeps its own X" precedent <see cref="CatalogInstallShell"/>'s own
    /// remarks describe for other per-controller specifics).</summary>
    static bool IsUnderCanonicalRoot(string candidate, string canonicalRoot) =>
        candidate == canonicalRoot || candidate.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    static ProblemDetails JinglePackNotFoundProblem(string slug) => new()
    {
        Status = StatusCodes.Status404NotFound,
        Title = "Not found.",
        Type = NotFoundType,
        Detail = $"No installed jingle pack with slug \"{slug}\" exists.",
    };

    static ProblemDetails AdsLibraryMissingProblem(string slug) => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "Jingle pack install failed.",
        Type = InstallFailedType,
        Detail = $"\"{slug}\" could not be installed: the ads library does not exist yet.",
    };

    static ProblemDetails EnrichmentFailedProblem(string slug, string file) => new()
    {
        Status = StatusCodes.Status422UnprocessableEntity,
        Title = "Jingle pack asset could not be enriched.",
        Type = AssetUnreadableType,
        Detail = $"\"{slug}\"'s asset \"{LogSafeText.Sanitize(file)}\" could not be measured/decoded and was withheld ({EnrichmentFailedSpecRef}).",
    };

    static ProblemDetails JingleFileWriteFailedProblem(string slug) => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "Jingle pack install failed.",
        Type = InstallFailedType,
        Detail = $"\"{slug}\" could not be written to disk. The previous install, if any, is unchanged.",
    };

    static ProblemDetails JinglePackInstallFailedProblem(string slug) => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "Jingle pack install failed.",
        Type = InstallFailedType,
        Detail = $"\"{slug}\" could not be installed. The previous install, if any, is unchanged.",
    };

    static ProblemDetails InstallRefusedProblem(string slug, IReadOnlyList<long> adSpotIds) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Jingle pack is referenced.",
        Type = InUseType,
        Detail = adSpotIds.Count > 0
            ? $"\"{slug}\" cannot be reinstalled: it would drop background music still referenced by ad spot(s) {string.Join(", ", adSpotIds)} — remove or re-render those first."
            : $"\"{slug}\" cannot be reinstalled: it would drop background music still referenced by an active ad spot — remove or re-render it first.",
    };

    static ProblemDetails UninstallRefusedProblem(string slug, IReadOnlyList<long> adSpotIds) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Jingle pack is referenced.",
        Type = InUseType,
        Detail = adSpotIds.Count > 0
            ? $"\"{slug}\" is still referenced by ad spot(s) {string.Join(", ", adSpotIds)} and cannot be uninstalled — remove or re-render those first."
            : $"\"{slug}\" is still referenced and cannot be uninstalled.",
    };

    /// <summary>One asset this install attempt staged and enriched, before it was ever moved into
    /// <see cref="PacksOptions.JingleRoot"/> — <see cref="GenWave.Core.Domain.Loudness"/>/<see cref="Cue"/> already carry
    /// <see cref="JingleCuePolicy.Apply"/>'s own role-based decision (SPEC F165.3), never the raw
    /// analyzer output for a <c>bed</c>/<c>sting</c>.</summary>
    readonly record struct StagedJingleAsset(
        CatalogJinglePackAsset Asset, string StagedPath, GenWave.Core.Domain.Loudness Loudness, CuePoints Cue, EnergyPoints? Energy,
        int DurationMs, int? SampleRate, short? Channels, int? BitrateKbps);

    /// <summary>One asset this install attempt moved into place, plus the previous install's own file
    /// it may have displaced to make room (mirrors <see cref="VoicePackController.WrittenVoiceFile"/>
    /// exactly, one field renamed) — <see cref="Path"/> is the FINAL path this attempt put in place;
    /// <see cref="DisplacedPath"/>, when not null, is a COPY of the file previously living at
    /// <see cref="Path"/>.</summary>
    readonly record struct WrittenJingleFile(string File, string Path, string? DisplacedPath);
}
