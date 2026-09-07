using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Host.Catalog;
using GenWave.Host.Options;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /api/voice-packs/{slug}/install</c> and <c>DELETE /api/voice-packs/{slug}</c> (SPEC
/// F164.5/F164.6/F166.4, STORY-395/396/398/401, PLAN T413) — installs/removes a Dean-curated voice
/// pack from the Community Catalog's <c>voice-pack</c> kind into the shared <c>voices</c> volume
/// Kokoro reads from. F79 shell, mirrors <see cref="FontPackController"/>/<see cref="AvatarPackController"/>'s
/// own shape: the same <see cref="AdminSurfaceAttribute"/> + <see cref="AuthorizationPolicies.Settings"/>
/// pairing, the same catalog-slug vocabulary, the same no-oracle <see cref="ProblemDetails"/> idioms
/// (F15.7 — no internal detail in a body).
///
/// <para>
/// <b>FLAT LAYOUT (PLAN T412 ruling, ARCHITECTURE.md:3327/3520-3523) — deliberately NOT the nested
/// <c>/voices/&lt;slug&gt;/&lt;voiceId&gt;.pt</c> shape SPEC/PLAN's own prose still describes.</b> Every
/// <c>.pt</c> file lands directly at <c>&lt;Packs:VoicesRoot&gt;/&lt;voiceId&gt;.pt</c> — kokoro-fastapi
/// 0.6.0 scans its voices directory non-recursively, and F166.4's global voice-id uniqueness fence
/// (enforced below, before any byte is fetched) makes a flat namespace safe. <c>station.voice_pack_voice.file</c>
/// stores the RELATIVE <c>&lt;voiceId&gt;.pt</c> name, never a full path — relocating
/// <see cref="PacksOptions.VoicesRoot"/> never breaks a stored row.
/// </para>
///
/// <para>
/// <b>Gate order (Install).</b> Route slug format (400) → catalog kill-switch (404, bare) → resolve
/// the entry (<see cref="CatalogInstallShell.ResolveEntryAsync"/>) → parse the manifest
/// (<see cref="CatalogVoicePackManifestSerializer.Deserialize"/>: reject ⇒ 400 malformed, engine is
/// only shape-checked there) → ENGINE CHECK, entirely HERE (T413 review round 2 finding B2 — the
/// closed-set membership check moved out of the serializer): the manifest's declared engine must both
/// be in <see cref="SupportedEngines"/> (mirrors db/45's own <c>check (engine in ('kokoro'))</c> on
/// <c>station.voice_pack</c> — the single seam gh-#614's future engine widening edits) AND equal
/// <see cref="PrimaryVoiceEngine"/>; either failing ⇒ 400, <c>Type = "not_supported_engine"</c>, SPEC
/// F164.2 → VOICE-ID COLLISION CHECK (stock via <see cref="ITtsVoiceLister"/>, installed via
/// <see cref="IVoicePackStore.FindInstalledVoiceIdsAsync"/>: any collision ⇒ 409, SPEC F166.4) → fetch
/// every declared asset (<see cref="CatalogInstallShell.FetchAllAssetsAsync"/>) → cross-check the
/// manifest's own declared files against what was actually fetched, plus the preview clip's own byte
/// ceiling (<see cref="PacksOptions.PreviewMaxBytes"/>) → write every <c>.pt</c> file atomically to
/// <see cref="PacksOptions.VoicesRoot"/> (temp-name-in-same-directory + 0644 + atomic rename; a
/// PRE-EXISTING target — a previous install's own live file — is displaced aside first, never
/// overwritten directly, T413 review round 2 finding B1; ANY failure restores every displaced file and
/// deletes every NEW file this attempt wrote) → ONE <see cref="IVoicePackStore.UpsertAsync"/>
/// transaction (a DB-side 23505 race unwinds identically) → on success, every displaced file is deleted
/// (the previous install is now genuinely superseded) →
/// <see cref="IVoiceListingCache.Invalidate"/> (SPEC F166.4's own "no restart" AC, STORY-398 AC4) → 200.
/// </para>
///
/// <para>
/// <b>The preview clip is verified, never written to disk (SPEC F164.4).</b> Its bytes are
/// hash-fetched through the same guarded door as every <c>.pt</c> file and capped separately at
/// <see cref="PacksOptions.PreviewMaxBytes"/> (a much tighter ceiling than a voice file's own transport
/// cap) — but only its already-index-pinned <see cref="CatalogInstallShell.CatalogFetchedAsset.Sha256"/>
/// is stored (<c>station.voice_pack_voice.preview_sha</c>, one copy per voice row); the shelf plays a
/// pack's preview straight off the catalog, never off this station's own disk, so nothing but a
/// Kokoro <c>.pt</c> file may ever land in <see cref="PacksOptions.VoicesRoot"/>.
/// </para>
///
/// <para>
/// <b>DELETE runs the in-DELETE guard (SPEC F164.6, STORY-401).</b> <see cref="IVoicePackStore.DeleteAsync"/>'s
/// own <c>WHERE NOT EXISTS</c> clauses are part of the SAME statement that removes the row — a
/// referencing <c>ad_spot.voice_plan</c> entry or <c>persona.voice</c> row still wins even if it is
/// inserted between this request's own guard read and its delete (see that member's own remarks). A
/// clean delete unlinks each voice's <c>.pt</c> file (a missing file is a no-op — idempotent by
/// construction) and invalidates the voice-listing cache before returning 204.
/// </para>
/// </summary>
[ApiController]
[Route("api/voice-packs")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.Settings)]
public sealed class VoicePackController(
    CatalogProxyService catalogProxyService,
    CommunityCatalogAccessor catalogAccessor,
    IVoicePackStore voicePackStore,
    PrimaryVoiceEngine primaryVoiceEngine,
    ITtsVoiceLister voiceLister,
    IVoiceListingCache voiceListingCache,
    IOptions<PacksOptions> packsOptions,
    ILogger<VoicePackController> logger) : ControllerBase
{
    /// <summary>App-side ceiling on the RUNNING total across every asset one voice-pack entry declares
    /// (SPEC F164.5) — 8 MiB, well clear of a stock pack's own measured 16 × ~524 KiB voices plus one
    /// preview clip. Mirrors <see cref="FontPackController"/>/<see cref="AvatarPackController"/>'s own
    /// per-controller-owned <c>MaxPackBytes</c> precedent — <see cref="CatalogInstallShell"/> only ever
    /// receives this as a parameter, never owns a whole-pack ceiling itself.</summary>
    const long MaxPackBytes = 8 * 1024 * 1024;
    const string MaxPackBytesSpecRef = "F164.5";

    /// <summary>SPEC reference for the PREVIEW clip's own, much tighter byte ceiling
    /// (<see cref="PacksOptions.PreviewMaxBytes"/>) — a distinct spec clause from
    /// <see cref="MaxPackBytesSpecRef"/>'s whole-pack transport cap (T413 review round 1 finding
    /// F4).</summary>
    const string PreviewMaxBytesSpecRef = "F164.4";

    /// <summary>SPEC F164.2's own machine-readable discriminator (STORY-396 AC2,
    /// <c>TheProblemDetailsTypeNamesTheEngineMismatch</c>) — the one <see cref="ProblemDetails.Type"/>
    /// this controller sets; every other body on this type follows the house
    /// Status/Title/Detail-only convention (no other refusal here needs machine discrimination beyond
    /// its own status code).</summary>
    const string NotSupportedEngineType = "not_supported_engine";

    /// <summary>The closed set of engines this database will even hold (T413 review round 2 finding
    /// B2) — mirrors db/45's own <c>check (engine in ('kokoro'))</c> on <c>station.voice_pack</c>
    /// EXACTLY: the single seam gh-#614's future engine widening edits, and the only place this list
    /// lives (<see cref="CatalogVoicePackManifestSerializer"/> only shape-checks the token now — see
    /// that type's own remarks). A manifest naming an engine outside this set is refused with the SAME
    /// <c>not_supported_engine</c> 400 an engine MISMATCH gets — "this database can't hold it" and
    /// "this station doesn't run it" are both instances of "this station cannot install this pack".</summary>
    static readonly string[] SupportedEngines = [DependencyNames.Kokoro];

    // 0644 — kokoro reads the shared volume as uid 1000 while this api container runs as root; .NET's
    // own File.Move/WriteAllBytes make no cross-platform promise about the mode a new file lands with.
    static readonly UnixFileMode VoiceFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    /// <summary>
    /// GET /api/voice-packs (STORY-397, PLAN T418) — every installed voice pack's slug and display
    /// name, for the catalog shelf's own "Installed" chip and detail panel; nothing else (no voice
    /// roster, no engine, no preview file name — the shelf already has those off the catalog entry
    /// itself, this route exists only to say WHICH slugs are installed). A row whose stored
    /// <c>definition</c> fails <see cref="CatalogVoicePackManifestSerializer.Deserialize"/> still
    /// appears, with <see cref="InstalledPackSummaryDto.PackName"/> falling back to the slug itself
    /// and a WARN logged naming the slug (see that type's own remarks for why this differs from
    /// <c>AttributionsController</c>'s skip-and-log posture) — never a 500, never a silently missing
    /// row.
    ///
    /// <para>
    /// <b>Route-set obligation (T200 review finding N7, mirrored at T418).</b> This is the first
    /// <c>GET</c> under <c>api/voice-packs</c> — <c>Story278_ThemeCatalogIsolation.cs</c>'s own
    /// route-set pin (its <c>ScenarioNoNewPublicRoute.KnownCatalogAndThemeRoutes</c>) is extended to
    /// include it, with the SAME class-level <see cref="AdminSurfaceAttribute"/>+
    /// <see cref="AuthorizationPolicies.Settings"/> pairing every route on this controller already
    /// carries — a plain <c>[HttpGet]</c> action change would otherwise trip that file's exact-match
    /// assertion, exactly as the finding asked for.
    /// </para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await voicePackStore.ListAsync(ct);
        var summaries = rows
            .OrderBy(row => row.Slug, StringComparer.Ordinal)
            .Select(row =>
            {
                var manifest = CatalogVoicePackManifestSerializer.Deserialize(row.DefinitionJson);
                if (manifest is null)
                {
                    logger.LogWarning(
                        "Voice pack listing found a stored definition that failed to re-parse slug={Slug} — " +
                        "the row still lists, with packName falling back to the slug itself",
                        LogSanitize.Strip(row.Slug));
                }

                return new InstalledPackSummaryDto(row.Slug, manifest?.PackName ?? row.Slug);
            })
            .ToArray();

        return Ok(summaries);
    }

    /// <summary>
    /// POST /api/voice-packs/{slug}/install — see this class's own remarks for the full gate order and
    /// the reasoning behind each one. Reads top-down: gate (<see cref="ResolveGatedManifestAsync"/>) →
    /// collision check → fetch → cross-check → write → one DB transaction → response (T413 review
    /// round 3 finding F5 — the slug/kill-switch/resolve/deserialize/engine gate block used to run
    /// inline here; extracted to its own method so this one reads as a single pipeline).
    /// </summary>
    [HttpPost("{slug}/install")]
    public async Task<IActionResult> Install(string slug, CancellationToken ct)
    {
        var (gateError, contentOrNull, manifestOrNull) = await ResolveGatedManifestAsync(slug, ct);
        if (gateError is not null)
            return gateError;
        if (contentOrNull is not { } content || manifestOrNull is not { } manifest)
            throw new UnreachableException("ResolveGatedManifestAsync returned neither an error nor a resolved manifest.");

        // VOICE-ID COLLISION CHECK (SPEC F166.4) — before any asset is fetched: a doomed install has no
        // business spending bandwidth on the catalog's own guarded door first.
        var collisionError = await CheckVoiceIdCollisionAsync(slug, manifest, ct);
        if (collisionError is not null)
            return collisionError;

        var (assetsError, fetchedAssetsOrNull) = await CatalogInstallShell.FetchAllAssetsAsync(
            catalogProxyService, slug, content,
            new CatalogInstallShell.PackFetchPolicy(
                CatalogEntryKind.VoicePack, CatalogIndexValidator.MaxVoiceFileBytes, MaxPackBytes, MaxPackBytesSpecRef),
            ct);
        if (assetsError is not null)
            return assetsError;
        if (fetchedAssetsOrNull is not { } fetchedAssets)
            throw new UnreachableException("CatalogInstallShell.FetchAllAssetsAsync returned neither an error nor a fetched-asset map.");

        var (crossCheckError, previewFile) = CrossCheckManifestAssets(slug, manifest, fetchedAssets);
        if (crossCheckError is not null)
            return crossCheckError;
        if (previewFile is not { } preview)
            throw new UnreachableException("CrossCheckManifestAssets returned neither an error nor a preview file name.");

        var previewSha = fetchedAssets[preview].Sha256;
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packsOptions.Value.VoicesRoot));

        var (writeError, writtenOrNull) = await WriteVoiceFilesAsync(slug, canonicalRoot, manifest.Voices, fetchedAssets, ct);
        if (writeError is not null)
            return writeError;
        if (writtenOrNull is not { } written)
            throw new UnreachableException("WriteVoiceFilesAsync returned neither an error nor a written-file list.");

        var voiceInputs = manifest.Voices
            .Select(voice => new VoicePackVoiceInput(voice.VoiceId, voice.File, voice.Gender, voice.Age, previewSha))
            .ToArray();

        VoicePackUpsertResult upsertResult;
        try
        {
            upsertResult = await voicePackStore.UpsertAsync(slug, manifest.Engine, content.ManifestJson, slug, voiceInputs, ct);
        }
        catch (Exception ex)
        {
            // ANY store failure after the .pt writes above — not just the 23505 race the switch
            // below maps to a 409 — must unwind what this attempt just wrote AND restore whatever it
            // displaced to make room for it (T413 review round 1 finding F2 / round 2 finding B1),
            // and that includes a client-disconnect/command-cancellation surfacing as
            // OperationCanceledException (T413 review round 3 finding F1): HttpContext.RequestAborted
            // can fire at any point across the whole DB round-trip a disconnect spans, and Npgsql
            // surfaces command cancellation as this same exception type — an unwind that only ran for
            // "everything except a cancel" left exactly the orphaned-.pt-with-no-DB-row state this
            // whole method exists to prevent. db/45's own `check (engine in ('kokoro'))` is one
            // reachable source of a non-cancellation failure here too, and F164.5's all-or-nothing
            // posture applies just as much to an unexpected store exception as it does to the
            // collision race below — including a RE-install, whose previous install's own file must
            // come back exactly as it was, never merely vanish while its own DB row survives.
            UnwindWritten(written);
            if (ex is OperationCanceledException)
                throw;

            // Nothing about a non-cancellation store failure is client input, so this is a generic
            // 500 (F15.7 — no raw Postgres text in the body).
            logger.LogError(ex, "Voice pack install failed while upserting the store slug={Slug}", LogSafeText.Sanitize(slug));
            return StatusCode(StatusCodes.Status500InternalServerError, VoicePackInstallFailedProblem(slug));
        }

        switch (upsertResult)
        {
            case VoicePackUpsertResult.Upserted:
                break;
            case VoicePackUpsertResult.VoiceIdCollision collision:
                // A concurrent install won the race between this request's own pre-check and its
                // write — the files this attempt just wrote never get to keep a home, and anything
                // it displaced to make room for them comes straight back (F164.5's all-or-nothing
                // posture applies across the write-then-DB boundary too, T413 review round 2 finding
                // B1).
                UnwindWritten(written);
                return Conflict(RaceVoiceIdCollisionProblem(slug, collision));
            default:
                throw new UnreachableException($"Unhandled {nameof(VoicePackUpsertResult)} case.");
        }

        // SUCCESS — the DB transaction has committed, so any file this attempt displaced to make room
        // for a re-install's own overwrite (T413 review round 2 finding B1) is now genuinely
        // superseded: delete it, after the commit, before the cache invalidates. A crash exactly here
        // leaves an orphaned `.prev-*` sibling on disk, never a torn install — the next re-install of
        // the same slug displaces it again like any other pre-existing target, and uninstall only ever
        // unlinks the names the DB row itself carries.
        foreach (var file in written)
        {
            if (file.DisplacedPath is { } displacedPath)
                TryDelete(displacedPath);
        }

        voiceListingCache.Invalidate();

        logger.LogInformation(
            "Voice pack installed slug={Slug} packName={PackName} voiceCount={VoiceCount}",
            LogSafeText.Sanitize(slug), LogSafeText.Sanitize(manifest.PackName), manifest.Voices.Count);

        return Ok(new VoicePackInstallResponse(
            slug, manifest.PackName, manifest.Voices.Select(voice => voice.VoiceId).ToArray()));
    }

    /// <summary>
    /// Route slug format (400) → catalog kill-switch (404, bare) → resolve the catalog entry
    /// (<see cref="CatalogInstallShell.ResolveEntryAsync"/>) → parse the manifest
    /// (<see cref="CatalogVoicePackManifestSerializer.Deserialize"/>: reject ⇒ 400 malformed, engine is
    /// only shape-checked there) → ENGINE CHECK (SPEC F164.2, T413 review round 2 finding B2): the
    /// manifest's declared engine must both be in <see cref="SupportedEngines"/> (mirrors db/45's own
    /// <c>check (engine in ('kokoro'))</c> — the single seam gh-#614's future engine widening edits)
    /// AND equal <see cref="PrimaryVoiceEngine"/>; either failing ⇒ 400, <c>Type =
    /// "not_supported_engine"</c>.
    ///
    /// <para>
    /// Each arm of the engine check is independently load-bearing (T413 review round 3 finding F2) —
    /// a station whose OWN primary engine also sits outside <see cref="SupportedEngines"/> (there is
    /// exactly one other engine token this codebase knows today, <c>"piper"</c>) would pass the
    /// primary-engine-equality arm trivially against a pack declaring that SAME engine; only the
    /// closed-set arm refuses that install (see Story396's own
    /// <c>ScenarioEngineOutsideTheClosedSetRefuses</c>, the fact proving a piper-primary station still
    /// refuses a piper-declared pack).
    /// </para>
    ///
    /// <para>
    /// Extracted out of <see cref="Install"/> (T413 review round 3 finding F5) so that method itself
    /// reads as a single top-down pipeline: gate → collision check → fetch → cross-check → write → one
    /// DB transaction → response.
    /// </para>
    /// </summary>
    async Task<(IActionResult? Error, CatalogEntryContent? Content, CatalogVoicePackManifest? Manifest)> ResolveGatedManifestAsync(
        string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return (BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length)), null, null);

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return (BadRequest(CatalogInstallShell.BadSlugProblem(slug)), null, null);

        if (!catalogAccessor.IsEnabled)
            return (CatalogInstallShell.DisabledSurfaceResult(Response), null, null);

        var (entryError, entryContent) = await CatalogInstallShell.ResolveEntryAsync(
            catalogProxyService, CatalogEntryKind.VoicePack, slug, ct);
        if (entryError is not null)
            return (entryError, null, null);
        if (entryContent is not { } content)
            throw new UnreachableException("CatalogInstallShell.ResolveEntryAsync returned neither an error nor content.");

        var manifest = CatalogVoicePackManifestSerializer.Deserialize(content.ManifestJson);
        if (manifest is null)
            return (BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.VoicePack, slug)), null, null);

        if (Array.IndexOf(SupportedEngines, manifest.Engine) < 0 ||
            !string.Equals(manifest.Engine, primaryVoiceEngine.DependencyName, StringComparison.Ordinal))
            return (BadRequest(NotSupportedEngineProblem(slug, manifest.Engine, primaryVoiceEngine.DependencyName)), null, null);

        return (null, content, manifest);
    }

    /// <summary>
    /// DELETE /api/voice-packs/{slug} — see this class's own remarks for the full contract
    /// <see cref="IVoicePackStore.DeleteAsync"/> enforces.
    /// </summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Uninstall(string slug, CancellationToken ct)
    {
        if (slug.Length > CatalogInstallShell.MaxSlugLength)
            return BadRequest(CatalogInstallShell.SlugTooLongProblem(slug.Length));

        if (!CatalogInstallShell.SlugFormat().IsMatch(slug))
            return BadRequest(CatalogInstallShell.BadSlugProblem(slug));

        var result = await voicePackStore.DeleteAsync(slug, ct);
        switch (result)
        {
            case VoicePackDeleteResult.Deleted deleted:
                UnlinkVoiceFiles(slug, deleted.Files);
                voiceListingCache.Invalidate();
                logger.LogInformation("Voice pack uninstalled slug={Slug}", LogSafeText.Sanitize(slug));
                return NoContent();
            case VoicePackDeleteResult.NotFound:
                return NotFound(CatalogInstallShell.UnknownInstalledPackProblem(CatalogEntryKind.VoicePack, slug));
            case VoicePackDeleteResult.Referenced referenced:
                logger.LogWarning(
                    "Voice pack uninstall refused slug={Slug} adSpotCount={AdSpotCount} personaCount={PersonaCount}",
                    LogSafeText.Sanitize(slug), referenced.AdSpotIds.Count, referenced.PersonaNames.Count);
                return Conflict(ReferencedProblem(slug, referenced.AdSpotIds, referenced.PersonaNames));
            default:
                throw new UnreachableException($"Unhandled {nameof(VoicePackDeleteResult)} case.");
        }
    }

    /// <summary>
    /// SPEC F166.4 — every candidate voice id this manifest declares is checked against BOTH the
    /// live stock roster (<see cref="ITtsVoiceLister.ListVoicesAsync"/>, TTL-cached — acceptable
    /// because stock ids never change) and every OTHER already-installed pack's own voice ids
    /// (<see cref="IVoicePackStore.FindInstalledVoiceIdsAsync"/>, excluding this same slug — a
    /// re-install of the pack already holding these ids is an upsert, never a collision).
    ///
    /// <para>
    /// <b>The stock set needs the SAME self-exclusion (T413 review round 1 finding F1).</b> Once a
    /// pack is installed, its <c>.pt</c> files live on the same flat, shared volume Kokoro itself
    /// scans (PLAN T412), so Kokoro's OWN <c>/v1/audio/voices</c> listing reports them as "stock" too
    /// — without subtracting <see cref="IVoicePackStore.FindVoiceIdsForPackAsync"/>'s own result from
    /// <paramref name="slug"/>'s candidate ids here, a re-install of the SAME pack would be refused as
    /// "used by stock", even though nothing else has ever installed those ids.
    /// </para>
    /// </summary>
    async Task<IActionResult?> CheckVoiceIdCollisionAsync(string slug, CatalogVoicePackManifest manifest, CancellationToken ct)
    {
        var candidateIds = manifest.Voices.Select(voice => voice.VoiceId).ToArray();

        var installedOwners = await voicePackStore.FindInstalledVoiceIdsAsync(candidateIds, slug, ct);
        var ownerByVoiceId = installedOwners.ToDictionary(owner => owner.VoiceId, owner => owner.OwnerSlug, StringComparer.Ordinal);

        var ownVoiceIds = new HashSet<string>(await voicePackStore.FindVoiceIdsForPackAsync(slug, ct), StringComparer.Ordinal);

        var stockIds = await voiceLister.ListVoicesAsync(ct);
        var stockIdSet = new HashSet<string>(stockIds, StringComparer.Ordinal);

        var collisions = new List<(string VoiceId, string Owner)>();
        foreach (var voiceId in candidateIds)
        {
            if (ownerByVoiceId.TryGetValue(voiceId, out var ownerSlug))
                collisions.Add((voiceId, ownerSlug));
            else if (stockIdSet.Contains(voiceId) && !ownVoiceIds.Contains(voiceId))
                collisions.Add((voiceId, "stock"));
        }

        return collisions.Count > 0 ? Conflict(VoiceIdCollisionProblem(slug, collisions)) : null;
    }

    /// <summary>
    /// Confirms <paramref name="manifest"/>'s own declared <c>preview</c> equals THIS entry's actual
    /// <paramref name="slug"/> (context <see cref="CatalogVoicePackManifestSerializer"/> does not have
    /// — see that type's own remarks), that every declared file (preview plus one per voice) was
    /// actually fetched, and that the preview clip sits under its own, tighter byte ceiling
    /// (<see cref="PacksOptions.PreviewMaxBytes"/> — <see cref="CatalogInstallShell.FetchAllAssetsAsync"/>'s
    /// own per-asset ceiling is the wider voice-file transport cap, SPEC F164.5).
    /// </summary>
    (IActionResult? Error, string? PreviewFile) CrossCheckManifestAssets(
        string slug, CatalogVoicePackManifest manifest, Dictionary<string, CatalogInstallShell.CatalogFetchedAsset> fetchedAssets)
    {
        var expectedPreview = $"{slug}.preview.mp3";
        if (!string.Equals(manifest.Preview, expectedPreview, StringComparison.Ordinal))
            return (BadRequest(CatalogInstallShell.MalformedManifestProblem(CatalogEntryKind.VoicePack, slug)), null);

        if (!fetchedAssets.TryGetValue(manifest.Preview, out var previewAsset))
            return (BadRequest(CatalogInstallShell.UndeclaredManifestAssetProblem(CatalogEntryKind.VoicePack, slug, manifest.Preview)), null);

        if (previewAsset.Bytes.LongLength > packsOptions.Value.PreviewMaxBytes)
            return (BadRequest(PreviewTooLargeProblem(slug, previewAsset.Bytes.LongLength, packsOptions.Value.PreviewMaxBytes)), null);

        foreach (var voice in manifest.Voices)
        {
            if (!fetchedAssets.ContainsKey(voice.File))
                return (BadRequest(CatalogInstallShell.UndeclaredManifestAssetProblem(CatalogEntryKind.VoicePack, slug, voice.File)), null);
        }

        return (null, manifest.Preview);
    }

    /// <summary>
    /// Writes every voice's <c>.pt</c> file atomically (temp name in the SAME directory, 0644, then an
    /// atomic rename-over) under <paramref name="canonicalRoot"/> — a separator-aware canonical-root
    /// re-assertion on every composed target guards the write even though <c>voice.File</c> can only
    /// ever be <c>voiceId + ".pt"</c> by construction (<see cref="SettingValidator.VoiceIdFormat"/>
    /// already forbids <c>/</c>/<c>..</c> in a voice id — this is defense-in-depth, mirrors
    /// <c>FileActionExecutor</c>'s own canonical-root idiom, reimplemented here rather than referenced
    /// across the Host/MediaLibrary project boundary).
    ///
    /// <para>
    /// <b>A pre-existing target is COPIED aside, never overwritten blind (T413 review round 2 finding
    /// B1, round 3 finding F3).</b> A re-install of an already-installed slug (F164.5's own upsert
    /// posture) targets the SAME path a previous, still-live install wrote — if this attempt's own
    /// write landed directly on top of it and a LATER step (a sibling voice's write, or the DB
    /// transaction) then failed, the previous install's own bytes would already be gone while its DB
    /// row survived untouched: kokoro would stop serving an id the database still claims. So a
    /// pre-existing target is first COPIED (not moved) to an unpredictable sibling name in the SAME
    /// jailed directory (<c>&lt;target&gt;.prev-&lt;guid&gt;</c>) — <see
    /// cref="WrittenVoiceFile.DisplacedPath"/> records it — and only then does the new temp file get
    /// renamed OVER <paramref name="canonicalRoot"/>'s target (<c>overwrite: true</c>), one atomic
    /// syscall. A copy-then-rename-over means the name at <c>target</c> is never briefly absent the
    /// way a move-then-move-back would leave it (round 3 finding F3 — a concurrent kokoro scan/open in
    /// that window would ENOENT); it is always either the previous install's bytes or this attempt's
    /// new ones. ANY failure (this method's own catch, or either post-write exit in <see
    /// cref="Install"/>) unwinds through <see cref="UnwindWritten"/>: when a previous install's file
    /// was displaced, the displaced COPY is renamed straight back OVER the new one — the SAME atomic
    /// rename-over, so the name is never briefly absent on the unwind path either; only when nothing
    /// was displaced does the new file simply get deleted. Only once the WHOLE install (write, then
    /// the DB transaction) has actually committed does <see cref="Install"/> delete the displaced
    /// copies for good — true all-or-nothing across the write-then-DB boundary, never a partial
    /// upgrade.
    /// </para>
    ///
    /// <para>
    /// This method's own <see cref="OperationCanceledException"/> catch arm mirrors <see
    /// cref="Install"/>'s upsert-catch arm exactly (unwind, then rethrow rather than answer with a
    /// 500 — T413 review round 4 finding L1) — but no fact drives it directly the way
    /// <c>ACancelledUpsertDuringAReinstallLeavesThePreviousInstallUntouched</c> drives the upsert arm:
    /// the write below is a direct <c>System.IO.File</c> call with no seam to inject a cancelling fake
    /// through (unlike <see cref="IVoicePackStore"/>'s own fake for the upsert arm), so this arm is
    /// verified by reading against its mirrored, fact-covered sibling rather than by a dedicated
    /// mutation-tested fact of its own.
    /// </para>
    /// </summary>
    async Task<(IActionResult? Error, IReadOnlyList<WrittenVoiceFile>? Written)> WriteVoiceFilesAsync(
        string slug, string canonicalRoot, IReadOnlyList<CatalogVoicePackVoice> voices,
        Dictionary<string, CatalogInstallShell.CatalogFetchedAsset> fetchedAssets, CancellationToken ct)
    {
        var written = new List<WrittenVoiceFile>(voices.Count);
        string? pendingTemp = null;
        // The CURRENT voice's own write, once it has displaced a previous install's file aside but
        // before the rename-over that would fold it into `written` has completed (T413 review round 3
        // finding F5 — a single nullable entry replaces the two loose strings this used to track
        // separately).
        WrittenVoiceFile? pending = null;
        try
        {
            foreach (var voice in voices)
            {
                var asset = fetchedAssets[voice.File];
                var target = Path.GetFullPath(Path.Combine(canonicalRoot, voice.File));
                if (!IsUnderCanonicalRoot(target, canonicalRoot))
                    throw new UnreachableException($"Voice file target escaped the voices root for voice id \"{voice.VoiceId}\".");

                pendingTemp = $"{target}.{Guid.NewGuid():N}.tmp";
                await System.IO.File.WriteAllBytesAsync(pendingTemp, asset.Bytes, ct);
                if (!OperatingSystem.IsWindows())
                    System.IO.File.SetUnixFileMode(pendingTemp, VoiceFileMode);

                if (System.IO.File.Exists(target))
                {
                    // A PREVIOUS install's own live file — copy it aside rather than move it (T413
                    // review round 3 finding F3): the rename-over below is atomic, so copying (rather
                    // than moving) here means `target`'s own name is never briefly absent.
                    var displacedPath = $"{target}.prev-{Guid.NewGuid():N}";
                    System.IO.File.Copy(target, displacedPath);
                    if (!OperatingSystem.IsWindows())
                        System.IO.File.SetUnixFileMode(displacedPath, VoiceFileMode);
                    pending = new WrittenVoiceFile(voice.VoiceId, target, displacedPath);
                }

                // overwrite: true — an atomic rename-OVER when a previous install's own file is still
                // at `target` (its bytes already safe at `pending.DisplacedPath` above); a plain
                // rename when nothing was there yet. Either way `target`'s name is never missing
                // (T413 review round 3 finding F3).
                System.IO.File.Move(pendingTemp, target, overwrite: true);
                pendingTemp = null;

                // `pending` already holds this exact record when a previous file was displaced (T413
                // review round 4 nit — one construction instead of two identical ones).
                written.Add(pending ?? new WrittenVoiceFile(voice.VoiceId, target, null));
                pending = null;
            }

            return (null, written);
        }
        catch (Exception ex)
        {
            if (pendingTemp is not null)
                TryDelete(pendingTemp);

            // Unwind everything this attempt itself touched: every voice this loop already fully
            // committed into `written`, PLUS the current, in-flight voice if the failure landed
            // between its own displace-aside and the commit that would have folded it into `written`.
            UnwindWritten(pending is { } current ? [.. written, current] : written);

            // A client-disconnect/command-cancellation surfacing as OperationCanceledException must
            // unwind exactly like any other failure here, then propagate as-is (T413 review round 3
            // finding F1) — swallowing it into a generic 500 would leave the framework's own
            // cancellation handling never seeing the cancel, and an unwind that only ran for
            // "everything except a cancel" is exactly how a `.pt` file used to be orphaned with no DB
            // row at all.
            if (ex is OperationCanceledException)
                throw;

            logger.LogError(ex, "Voice pack install failed while writing voice files slug={Slug}", LogSafeText.Sanitize(slug));
            return (StatusCode(StatusCodes.Status500InternalServerError, VoiceFileWriteFailedProblem(slug)), null);
        }
    }

    /// <summary>Unlinks every voice file a successful <see cref="IVoicePackStore.DeleteAsync"/> just
    /// named (read inside the SAME transaction that removed the rows, before the cascade). A missing
    /// file is a no-op (idempotent by construction — <see cref="TryDelete"/> swallows the "already
    /// gone" case exactly like every OTHER failure it swallows); a file this station's own root check
    /// rejects is logged and skipped rather than ever escaping the voices root.</summary>
    void UnlinkVoiceFiles(string slug, IReadOnlyList<string> files)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packsOptions.Value.VoicesRoot));
        foreach (var file in files)
        {
            var target = Path.GetFullPath(Path.Combine(canonicalRoot, file));
            if (!IsUnderCanonicalRoot(target, canonicalRoot))
            {
                logger.LogWarning(
                    "Voice pack uninstall skipped a file outside the voices root slug={Slug} file={File}",
                    LogSafeText.Sanitize(slug), LogSafeText.Sanitize(file));
                continue;
            }

            TryDelete(target);
        }
    }

    /// <summary>Unwinds every file THIS install attempt itself wrote (<see cref="WriteVoiceFilesAsync"/>'s
    /// own return value) — the ONE unwind path every post-write failure exit uses (T413 review round 1
    /// finding F2), so a store failure can never orphan a <c>.pt</c> file on the shared volume no
    /// unwind path already covered. When this attempt displaced a PREVIOUS install's own live file to
    /// make room for its own, the displaced COPY is renamed straight back OVER the new one — an atomic
    /// rename-over, so <see cref="WrittenVoiceFile.Path"/>'s name is never briefly absent on the
    /// unwind path either (T413 review round 4 finding B1: an unconditional delete-then-restore used
    /// to re-open exactly that window — target name gone until the restore's own rename landed). Only
    /// when nothing was displaced (a fresh install, nothing at that name before this attempt) does the
    /// new file simply get deleted. Either way a failed re-install leaves the previous install's own
    /// bytes, and its still-surviving DB row, in exactly the state they were in before this attempt
    /// ever started. True all-or-nothing, never a partial upgrade.</summary>
    void UnwindWritten(IReadOnlyList<WrittenVoiceFile> written)
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
            logger.LogWarning(ex, "Voice pack install/uninstall could not remove file {Path}", LogSafeText.Sanitize(path));
        }
    }

    /// <summary>Moves a previous install's own file, displaced aside during this attempt's write
    /// (<see cref="WriteVoiceFilesAsync"/>), straight back into place — the restore half of
    /// <see cref="UnwindWritten"/>'s all-or-nothing posture (T413 review round 2 finding B1). A
    /// failure here (the displaced file itself went missing — e.g. a concurrent uninstall of the SAME
    /// slug, which the DB-level guard makes vanishingly unlikely but not impossible) is logged and
    /// swallowed exactly like <see cref="TryDelete"/>'s own "already gone" case: there is nothing
    /// further this unwind can safely do about it.</summary>
    void TryRestore(string displacedPath, string target)
    {
        try
        {
            System.IO.File.Move(displacedPath, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Name BOTH paths (T413 review round 3 finding F4) — the target alone tells an operator
            // what SHOULD be there, not where their bytes actually still are: the displaced copy at
            // {DisplacedPath} is what a failed restore leaves behind for manual recovery.
            logger.LogWarning(
                ex, "Voice pack install could not restore the previous file at {Path} from its displaced copy at {DisplacedPath}",
                LogSafeText.Sanitize(target), LogSafeText.Sanitize(displacedPath));
        }
    }

    /// <summary>Separator-aware canonical-root check — <c>FileActionExecutor.IsUnderCanonicalRoot</c>'s
    /// own idiom, reimplemented here (not referenced across the Host/MediaLibrary project boundary):
    /// a bare <c>StartsWith(root)</c> would wrongly admit a sibling directory sharing the same
    /// prefix (e.g. <c>/voices-evil</c> against a root of <c>/voices</c>).</summary>
    static bool IsUnderCanonicalRoot(string candidate, string canonicalRoot) =>
        candidate == canonicalRoot || candidate.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    /// <summary>Names both the pack's own declared engine and the station's actual primary engine,
    /// AND the closed set of engines this database can even hold (T413 review round 3 finding F2) —
    /// when the pack and station engine happen to be the SAME token (a station whose own primary
    /// engine sits outside <see cref="SupportedEngines"/> installing a pack declaring that same
    /// engine), naming only those two would read as a non-sequitur ("built for X, station runs X"),
    /// so the closed set is always spelled out too.</summary>
    static ProblemDetails NotSupportedEngineProblem(string slug, string packEngine, string stationEngine) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Type = NotSupportedEngineType,
        Title = "Voice pack engine not supported.",
        Detail = $"\"{slug}\" is built for the \"{packEngine}\" engine, but this station's primary voice engine " +
            $"is \"{stationEngine}\" (supported engines: {string.Join(", ", SupportedEngines)}).",
    };

    static ProblemDetails VoiceIdCollisionProblem(string slug, IReadOnlyList<(string VoiceId, string Owner)> collisions) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Voice id already installed.",
        Detail = $"\"{slug}\" cannot install: " +
            string.Join("; ", collisions.Select(collision =>
                $"\"{collision.VoiceId}\" is already used by {DescribeOwner(collision.Owner)}")) +
            ".",
    };

    static ProblemDetails RaceVoiceIdCollisionProblem(string slug, VoicePackUpsertResult.VoiceIdCollision collision) =>
        collision is { VoiceId: { } voiceId, OwnerSlug: { } ownerSlug }
            ? VoiceIdCollisionProblem(slug, [(voiceId, ownerSlug)])
            : new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Voice id already installed.",
                Detail = $"\"{slug}\" declares a voice id already used by another pack.",
            };

    static string DescribeOwner(string owner) =>
        string.Equals(owner, "stock", StringComparison.Ordinal) ? "stock" : $"pack \"{owner}\"";

    static ProblemDetails PreviewTooLargeProblem(string slug, long actualBytes, int maxBytes) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Voice pack preview exceeds the size ceiling.",
        Detail = $"\"{slug}\"'s preview clip is {actualBytes} bytes, over the {maxBytes}-byte ceiling ({PreviewMaxBytesSpecRef}).",
    };

    static ProblemDetails VoiceFileWriteFailedProblem(string slug) => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "Voice pack install failed.",
        Detail = $"\"{slug}\" could not be written to disk. The previous install, if any, is unchanged.",
    };

    /// <summary>The store-side counterpart of <see cref="VoiceFileWriteFailedProblem"/> — an
    /// <see cref="IVoicePackStore.UpsertAsync"/> failure AFTER the <c>.pt</c> writes already
    /// succeeded (T413 review round 1 finding F2): no internal detail (Postgres SQLSTATE, message
    /// text) ever reaches this body (F15.7) — the files this attempt wrote are already unwound by
    /// the caller before this is returned (T413 review round 2 finding B1: a re-install's own
    /// unwind restores whatever it displaced, so a previous install's files survive intact too).</summary>
    static ProblemDetails VoicePackInstallFailedProblem(string slug) => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "Voice pack install failed.",
        Detail = $"\"{slug}\" could not be installed. The previous install, if any, is unchanged.",
    };

    static ProblemDetails ReferencedProblem(string slug, IReadOnlyList<long> adSpotIds, IReadOnlyList<string> personaNames) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Voice pack is referenced.",
        Detail = BuildReferencedDetail(slug, adSpotIds, personaNames),
    };

    static string BuildReferencedDetail(string slug, IReadOnlyList<long> adSpotIds, IReadOnlyList<string> personaNames)
    {
        var parts = new List<string>();
        if (adSpotIds.Count > 0)
            parts.Add($"ad spot(s) {string.Join(", ", adSpotIds)}");

        if (personaNames.Count > 0)
            parts.Add($"persona(s) {string.Join(", ", personaNames.Select(name => $"\"{LogSafeText.Sanitize(name)}\""))}");

        return parts.Count > 0
            ? $"\"{slug}\" is still referenced by {string.Join(" and ", parts)} and cannot be uninstalled — remove or reassign those first."
            : $"\"{slug}\" is still referenced and cannot be uninstalled.";
    }

    /// <summary>One voice's <c>.pt</c> file this install attempt wrote, plus the previous install's
    /// own file it may have displaced to make room (T413 review round 2 finding B1) — <see
    /// cref="Path"/> is the NEW file THIS attempt put in place; <see cref="DisplacedPath"/>, when
    /// not null, is a COPY of the file previously living at <see cref="Path"/> (T413 review round 3
    /// finding F3 — copied, not moved, so <see cref="Path"/>'s own name is never briefly absent), so a
    /// later failure (a sibling voice's own write, or the DB transaction) can restore it exactly,
    /// never merely delete a live pack's own bytes.</summary>
    readonly record struct WrittenVoiceFile(string VoiceId, string Path, string? DisplacedPath);
}
