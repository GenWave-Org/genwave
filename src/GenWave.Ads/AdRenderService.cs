namespace GenWave.Ads;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Logging;
using GenWave.Tts;

/// <summary>
/// Renders ONE approved ad spot to a ready, airable <c>library.media</c> row (SPEC F161.1-F161.3;
/// STORY-391; PLAN T401) — the first production consumer of <c>GenWave.Tts</c> from this project
/// (the <c>AdScriptWriter</c>/<c>AdScriptValidator</c> precedent kept Tts decoupled from Ads via
/// caller-supplied delegates instead; this class genuinely needs the real cast assembler, so
/// GenWave.Ads takes a real <c>ProjectReference</c> on GenWave.Tts — see the csproj's own remarks).
/// <see cref="RenderAsync"/> is a stateless, side-effecting pipeline: it never claims or ticks — that
/// is <c>AdSpotWorker</c>'s job (PLAN T402, a LATER task); this class just renders whatever spot it is
/// handed, once, to a terminal <see cref="AdState.Ready"/> or <see cref="AdState.Failed"/>.
///
/// <para>
/// <b>The render flow:</b> re-parse <see cref="AdSpot.Script"/> (already validated at generation/save
/// time — T399's <c>AdScriptValidator</c>; this is a structural re-parse only, never a re-validation)
/// → resolve the voice cast from <see cref="AdSpot.VoicePlan"/> (or the station default — see
/// <see cref="ResolveCast"/>'s own remarks) → resolve an optional bed (never a caller-supplied path —
/// the <c>SafeSegmentsController.ResolveBedAsync</c> precedent) → resolve the ads library id → author
/// through <see cref="CastSegmentAuthor"/>, whose <c>confirmAsync</c> delegate closes over
/// <see cref="IAdSpotStore.MarkReadyAsync"/> — the SAME "caller-supplied delegate" posture
/// <c>AdScriptWriter</c>'s own F160.1 rider established one task over, here crossing the OTHER
/// direction (Ads supplying the confirmation Tts cannot know about).
/// </para>
///
/// <para>
/// <b>Never throws out of <see cref="RenderAsync"/></b> (except a genuine
/// <see cref="OperationCanceledException"/> from host shutdown) — the <c>AdsLibrarySeeder</c> posture
/// applied to a per-spot render: T402's worker calls this once per tick, and one spot's render failure
/// (a bad script, an unreachable Kokoro, a Postgres blip) must never take the whole tick loop down.
/// Every failure path here reports through <see cref="IAdSpotStore.MarkFailedAsync"/> instead.
/// </para>
///
/// <para>
/// <b>The preview render (SPEC F174.4; STORY-424; PLAN T442) is a second, narrower entry point,
/// <see cref="RenderPreviewAsync"/></b> — it shares <see cref="RenderAsync"/>'s own
/// parse/bed/cast/tags assembly (see <c>BuildAssemblyRequestAsync</c>'s own remarks) but writes to a
/// standalone file under the preview root via <see cref="ICastSegmentAuthor.AssembleOnlyAsync"/>
/// rather than <see cref="ICastSegmentAuthor.AuthorAsync"/> — no <c>library.media</c> row, no
/// <see cref="AdState"/> transition either way; the caller's own preview job owns stamping the result.
/// </para>
/// </summary>
public sealed class AdRenderService(
    ICastSegmentAuthor author,
    IAdSpotStore spotStore,
    IAdminMediaLookup adminLookup,
    ILibraryRepository libraryRepository,
    IStationIdentityProvider stationIdentity,
    IOptionsMonitor<AdsOptions> adsOptions,
    AdSpotLocatorRoots locatorRoots,
    ILogger<AdRenderService> logger)
{
    /// <summary>
    /// PLAN T416 review F3+O3 (amends brief ruling R6): <paramref name="liveSettings"/> is the SAME
    /// <c>AdLiveSettingsReader.Read</c> result <see cref="AdSpotWorker"/> already reads once per tick
    /// for its own cast pick — handed in here rather than re-read a second time off a second
    /// <c>IConfiguration</c> dependency this class no longer carries. Passing the whole record (not a
    /// bare <c>double bedFadeSeconds</c>) keeps the ms→seconds unit conversion co-located inside
    /// <see cref="RenderCoreAsync"/>, exactly where <see cref="CastAssemblyRequest"/> is built, and
    /// leaves room for a later render-time Live setting to ride the SAME parameter without another
    /// signature change.
    /// </summary>
    internal async Task<AdRenderOutcome> RenderAsync(AdSpot spot, AdLiveSettings liveSettings, CancellationToken ct)
    {
        try
        {
            return await RenderCoreAsync(spot, liveSettings, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot {Id} render failed unexpectedly", spot.Id);
            return await FailAsync(spot.Id, $"render: unexpected {ex.GetType().Name}", ct);
        }
    }

    async Task<AdRenderOutcome> RenderCoreAsync(AdSpot spot, AdLiveSettings liveSettings, CancellationToken ct)
    {
        var outputDirectory = ResolveAdsRoot();
        var (request, failure) = await BuildAssemblyRequestAsync(spot, liveSettings, outputDirectory, ct);
        if (request is null)
            return await FailAsync(spot.Id, failure ?? "render: assembly request build failed", ct);

        var libraryId = await ResolveLibraryIdAsync(ct);
        if (libraryId is null)
            return await FailAsync(spot.Id, "render: the ads library does not exist yet", ct);

        var result = await author.AuthorAsync(
            request,
            buildInsert: assembled => BuildInsert(libraryId.Value, request.Tags, assembled),
            confirmAsync: (mediaId, confirmCt) => spotStore.MarkReadyAsync(spot.Id, mediaId, confirmCt),
            ct);

        if (!result.Succeeded)
            return await FailAsync(spot.Id, $"render: {result.FailureReason} — {result.FailureDetail}", ct);

        return AdRenderOutcome.Rendered;
    }

    /// <summary>
    /// The canonical, full-path <c>{AuthoredRoot}/ads</c> directory (SPEC F174.5; PLAN T445 —
    /// hoisted out of <see cref="RenderCoreAsync"/>'s own former inline <c>Path.Combine</c>) — the
    /// ONE construction site both the write-render path above and <see cref="PromotePreviewAsync"/>
    /// below share, mirroring <see cref="AdPreviewRoot.Resolve"/>'s own identical shape one type over
    /// (<c>Path.TrimEndingDirectorySeparator(Path.GetFullPath(...))</c>) so <see cref="AdPreviewRoot.IsUnder"/>
    /// re-assertions against either root compare like for like.
    /// </summary>
    string ResolveAdsRoot() =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(locatorRoots.AuthoredRoot, "ads")));

    /// <summary>
    /// Promotes <paramref name="spot"/>'s CURRENT preview file to a real, airable <c>library.media</c>
    /// row (SPEC F174.5; STORY-425; STORY-429 AC1; PLAN T445). The caller
    /// (<c>AdsController.Approve</c>) has already claimed <paramref name="spot"/> into
    /// <see cref="AdState.Rendering"/> (<see cref="IAdSpotStore.ClaimForPromotionAsync"/>) and
    /// re-asserted the preview is current before calling this — this method's own job is strictly the
    /// file move plus catalog landing, reusing <see cref="BuildInsert"/> UNCHANGED (the SAME
    /// title/artist stamps a write-mode render would produce, SPEC F161.3's own landing shape) and
    /// <see cref="ICastSegmentAuthor.LandAsync"/> (the SAME insert→confirm→eligible tail
    /// <see cref="RenderCoreAsync"/>'s own <see cref="ICastSegmentAuthor.AuthorAsync"/> call lands
    /// through) rather than a second, hand-kept landing path.
    ///
    /// <para>
    /// <b>Move, never copy (STORY-429 AC1).</b> <see cref="File.Move(string, string)"/> relocates the
    /// preview file directly onto its final ads-root path; there is no intermediate copy step, and no
    /// second file survives at <see cref="AdSpot.PreviewPath"/> once this returns
    /// <see cref="AdPromotionOutcome.Landed"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Both paths re-asserted under their own canonical root, in THIS method (the CodeQL
    /// path-injection guard's own "strong guard" shape, <see cref="AdPreviewRoot.IsUnder"/>'s own
    /// remarks — the same construction/check site <see cref="RenderCoreAsync"/>,
    /// <c>AdsController.PreviewWav</c>, and the guardian's own sweep all share).</b> The stored
    /// <see cref="AdSpot.PreviewPath"/> is untrusted the instant it crosses a storage boundary, exactly
    /// like every other stored path this project re-checks; the freshly-built destination gets the
    /// identical treatment even though this method itself just constructed it, so a future edit to
    /// <see cref="ResolveAdsRoot"/> can never silently widen where a promoted file can land.
    /// </para>
    ///
    /// <para>
    /// <b>Never <see cref="IAdSpotStore.MarkFailedAsync"/> (PLAN T445 ruling).</b> Unlike
    /// <see cref="RenderCoreAsync"/>'s own failure path, a failed promotion never marks the spot
    /// <see cref="AdState.Failed"/> — the row is already back to <see cref="AdState.Approved"/> by the
    /// time <see cref="AdSpot.PreviewPath"/> was ever real; the caller re-arms it instead
    /// (<see cref="IAdSpotStore.ReArmAsync"/>), so the operator can simply approve again once whatever
    /// blocked the move (a full disk, a path collision) is fixed, without first having to notice and
    /// retry a spurious "failed" spot.
    /// </para>
    ///
    /// <para>
    /// <b><see langword="public"/>, unlike <see cref="RenderAsync"/>/<see cref="RenderPreviewAsync"/>
    /// (PLAN T445).</b> Those two stay <see langword="internal"/> — <c>AdSpotWorker</c>, their only
    /// caller, lives in this same assembly. This method's only caller, <c>AdsController.Approve</c>,
    /// lives in <c>GenWave.Host</c> — an operator-driven, on-demand promotion of one specific,
    /// already-claimed row, never a background tick.
    /// </para>
    /// </summary>
    public async Task<AdPromotionOutcome> PromotePreviewAsync(AdSpot spot, CancellationToken ct)
    {
        if (spot.PreviewPath is not { } previewPath)
            return new AdPromotionOutcome.Failed("promote: spot carries no preview to promote", FileMoved: false);

        var previewRoot = AdPreviewRoot.Resolve(locatorRoots);
        var source = Path.GetFullPath(previewPath);
        if (!AdPreviewRoot.IsUnder(previewRoot, source))
        {
            logger.LogWarning(
                "Ad spot preview promotion found a preview path outside the preview root for spot {Id}", spot.Id);
            return new AdPromotionOutcome.Failed("promote: preview path escaped the preview root", FileMoved: false);
        }

        if (!File.Exists(source))
            return new AdPromotionOutcome.Failed("promote: preview file is gone", FileMoved: false);

        var libraryId = await ResolveLibraryIdAsync(ct);
        if (libraryId is null)
            return new AdPromotionOutcome.Failed("promote: the ads library does not exist yet", FileMoved: false);

        var adsRoot = ResolveAdsRoot();
        var destination = Path.GetFullPath(Path.Combine(adsRoot, $"{Guid.NewGuid():N}.wav"));
        if (!AdPreviewRoot.IsUnder(adsRoot, destination))
        {
            logger.LogWarning(
                "Ad spot preview promotion built a destination path outside the ads root for spot {Id}", spot.Id);
            return new AdPromotionOutcome.Failed("promote: destination path escaped the ads root", FileMoved: false);
        }

        // `moved` tracks whether File.Move below actually completed — the ONE fact the outer catch
        // needs to answer AdPromotionOutcome.Failed's own FileMoved question correctly regardless of
        // which step past the move (measure, or the land tail) is what actually threw or failed.
        var moved = false;
        try
        {
            Directory.CreateDirectory(adsRoot);
            File.Move(source, destination);
            moved = true;

            var assembled = await author.MeasureAsync(destination, ct);
            var tags = new AudioTags(stationIdentity.Current.Name, spot.Title);

            // author.LandAsync's own tail (the SAME insert→confirm→eligible mechanism RenderCoreAsync's
            // own AuthorAsync call lands through, hoisted at PLAN T445 into ICastSegmentAuthor.LandAsync)
            // already deletes the moved file on an insert failure — a Failed result below never leaves
            // an orphaned file at `destination` on THAT particular path.
            var result = await author.LandAsync(
                assembled,
                buildInsert: a => BuildInsert(libraryId.Value, tags, a),
                confirmAsync: (mediaId, confirmCt) => spotStore.MarkReadyAsync(spot.Id, mediaId, confirmCt),
                ct);

            return result.Succeeded
                ? new AdPromotionOutcome.Landed(result.MediaId)
                : new AdPromotionOutcome.Failed($"promote: {result.FailureReason} — {result.FailureDetail}", FileMoved: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot {Id} preview promotion failed unexpectedly", spot.Id);

            // PLAN T445 ruling: any throw past the move reaches here — the insert arm is the
            // one LandAsync cleans up itself (see its own try/catch tail). Left alone, this would
            // leave the moved file sitting under the ads root with no preview stamp naming it —
            // nothing else ever sweeps that directory. Deleting the moved file here keeps
            // FileMoved: true honest: an already-committed row on such a path stays ineligible, the
            // posture CastSegmentAuthor's remarks accept, and a bare retry genuinely has nothing
            // left to promote without re-rendering.
            if (moved)
                DeleteIfExists(destination);

            return new AdPromotionOutcome.Failed($"promote: unexpected {ex.GetType().Name}", FileMoved: moved);
        }
    }

    static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup — mirrors CastSegmentAuthor/CrosstalkAssembler's own identical
            // precedent: a locked/undeletable file is a secondary concern, never worth masking the
            // real outcome this call is already returning.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup — see the IOException arm's own remarks.
        }
    }

    /// <summary>
    /// Renders <paramref name="spot"/> to a standalone preview WAV — never a catalog write (SPEC
    /// F174.4; STORY-424; PLAN T442): unlike <see cref="RenderAsync"/>, nothing on this path ever
    /// calls <see cref="IAdSpotStore.MarkFailedAsync"/> or <see cref="IAdSpotStore.MarkReadyAsync"/> —
    /// the spot's own <see cref="AdState"/> is untouched either way. The caller's own preview job
    /// stamps success (<c>preview_path</c>/<c>preview_at</c>/<c>preview_key</c>) or records failure
    /// (<c>job_error</c>) itself; a preview render failure is never a reason to fail the spot.
    /// </summary>
    /// <param name="sponsor">The spot's own sponsor, already resolved by the caller (this service has
    /// no <c>ISponsorStore</c> dependency of its own) — <see cref="AdPreviewKey.Compute"/> needs the
    /// full sponsor record, not merely the id every other render path carries.</param>
    internal async Task<AdPreviewOutcome> RenderPreviewAsync(
        AdSpot spot, Sponsor sponsor, AdLiveSettings live, CancellationToken ct)
    {
        try
        {
            return await RenderPreviewCoreAsync(spot, sponsor, live, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot {Id} preview render failed unexpectedly", spot.Id);
            return new AdPreviewOutcome.Failed($"preview: unexpected {ex.GetType().Name}");
        }
    }

    async Task<AdPreviewOutcome> RenderPreviewCoreAsync(
        AdSpot spot, Sponsor sponsor, AdLiveSettings live, CancellationToken ct)
    {
        var previewRoot = AdPreviewRoot.Resolve(locatorRoots);
        var (request, failure) = await BuildAssemblyRequestAsync(spot, live, previewRoot, ct);
        if (request is null)
            return new AdPreviewOutcome.Failed(failure ?? "preview: assembly request build failed");

        Directory.CreateDirectory(previewRoot);
        var assembled = await author.AssembleOnlyAsync(request, ct);
        if (assembled is not CrosstalkAssemblyResult.Assembled result)
        {
            var reason = assembled is CrosstalkAssemblyResult.Discarded discarded ? discarded.Reason : "assembly failed";
            return new AdPreviewOutcome.Failed($"preview: {reason}");
        }

        // The assembler encodes pcm_s16le into a .{Tts:Format} container (CastSegmentAuthor's own
        // ffmpeg invocation) — so only genuine wav bytes are ever labelled .wav below; a station
        // configured for a different Tts:Format hands back a differently-extensioned file here, and
        // renaming those bytes onto a ".wav" path would serve mislabeled audio rather than fail loudly.
        var assembledExtension = Path.GetExtension(result.Path).TrimStart('.');
        if (!string.Equals(assembledExtension, "wav", StringComparison.OrdinalIgnoreCase))
        {
            return new AdPreviewOutcome.Failed(
                $"preview needs Tts:Format=wav; the station renders {(assembledExtension.Length > 0 ? assembledExtension : "an unlabeled format")}");
        }

        var key = AdPreviewKey.Compute(spot, sponsor, live, adsOptions.CurrentValue.BedDuckDb);
        var finalPath = Path.Combine(previewRoot, $"{spot.Id}-{key}.wav");
        File.Move(result.Path, finalPath, overwrite: true);
        return new AdPreviewOutcome.Rendered(finalPath, key);
    }

    /// <summary>
    /// Builds the request both <see cref="RenderCoreAsync"/> and <see cref="RenderPreviewCoreAsync"/>
    /// hand to <see cref="ICastSegmentAuthor"/> — the SAME parse/bed/cast/tags/ceiling assembly either
    /// render needs, differing only in <paramref name="outputDirectory"/> and, on the write path, the
    /// ads library id <see cref="RenderCoreAsync"/> separately resolves once this call returns (PLAN
    /// T442 ruling: extracted so the preview render shares this shape exactly rather than
    /// re-diverging it by hand). A <see langword="null"/> <c>Request</c> means <c>Failure</c> carries
    /// the reason the caller's own failure path should report.
    /// </summary>
    async Task<(CastAssemblyRequest? Request, string? Failure)> BuildAssemblyRequestAsync(
        AdSpot spot, AdLiveSettings liveSettings, string outputDirectory, CancellationToken ct)
    {
        // A structural re-parse only (int.MaxValue as the per-line ceiling — the length rule was
        // already enforced at write time; re-checking it here would be re-validation, not rendering).
        var parsed = AdScriptParser.Parse(spot.Script ?? "", int.MaxValue);
        if (parsed is not AdScriptValidationResult.Accepted(var script))
        {
            var reason = parsed is AdScriptValidationResult.Refused refused
                ? refused.Violation.Reason
                : "unparseable script";
            return (null, $"render: stored script no longer parses ({reason})");
        }

        var (bed, bedFailure) = await ResolveBedAsync(spot.BedMediaId, ct);
        if (bedFailure is not null)
            return (null, bedFailure);

        var cast = ResolveCast(spot, script);
        var lines = script.Lines.Select(line => new CastLine(line.Tag, line.Text)).ToList();
        var tags = new AudioTags(stationIdentity.Current.Name, spot.Title);
        var ceilingSeconds = spot.SpotSeconds * (1 + adsOptions.CurrentValue.DurationToleranceRatio);

        // SPEC F168.4; STORY-403; PLAN T416 — Station:Ads:BedFadeMs is a Live setting,
        // handed in as liveSettings.BedFadeMs (the SAME AdLiveSettingsReader.Read result
        // AdSpotWorker's own cast-pick call already read once, earlier in the SAME tick — this class
        // no longer carries its own IConfiguration to re-read it a second time), stored in
        // milliseconds (SettingValidator's own unit) but CastAssemblyRequest/AudioMixRequest/
        // FfmpegAudioMixer all work in seconds throughout (BedDuckDb/BedPadSeconds precedent right
        // beside it) — converted here, once, at the one seam that actually crosses the unit boundary.
        var bedFadeSeconds = liveSettings.BedFadeMs / 1000.0;
        // Named, not positional, for BedFadeSeconds: CastAssemblyRequest carries an UNRELATED
        // BedPadSeconds member (Station:Safe:* padding, never set by Ads) between BedDuckDb and
        // BedFadeSeconds — a positional trailing arg here would silently land in the wrong slot.
        var request = new CastAssemblyRequest(
            lines, cast, ceilingSeconds, tags, outputDirectory, bed, adsOptions.CurrentValue.BedDuckDb,
            BedFadeSeconds: bedFadeSeconds);
        return (request, null);
    }

    /// <summary>
    /// Resolves the voice cast from <see cref="AdSpot.VoicePlan"/> (SPEC F161.2's own rider, PLAN
    /// T401 design decision). A malformed, absent, or entirely-unusable plan — every spot rendered
    /// before T403's editor exists — falls back to the SAME station voice for every tag: the honest,
    /// ship-today default, never a refusal (F158.1 "null is legal" holds here too: a station with no
    /// per-tag casting preference yet still gets a spot, voiced consistently in its own default
    /// voice, rather than no spot at all). T403's owner editor is where an operator sets a real,
    /// per-tag plan.
    /// </summary>
    IReadOnlyList<CastMember> ResolveCast(AdSpot spot, AdScript script)
    {
        var scriptTags = script.Lines.Select(line => line.Tag).Distinct(StringComparer.Ordinal).ToList();
        var stationVoice = new VoiceSpec(Engine: "", stationIdentity.Current.Voice, Pace: 1.0, Language: "en");

        var plan = ParseVoicePlan(spot.VoicePlan);
        if (plan is null)
            return scriptTags.Select(tag => new CastMember(tag, stationVoice)).ToList();

        var byTag = plan
            .GroupBy(entry => entry.Tag, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return scriptTags
            .Select(tag => new CastMember(
                tag,
                byTag.TryGetValue(tag, out var entry)
                    ? new VoiceSpec(Engine: "", entry.VoiceId, entry.Pace, Language: "en")
                    : stationVoice))
            .ToList();
    }

    /// <summary>
    /// Deserializes <see cref="AdSpot.VoicePlan"/>'s opaque jsonb text into <see cref="AdVoicePlanEntry"/>s
    /// — <see langword="null"/> on absence, malformed JSON, or once every entry has been dropped
    /// (never throws), so a corrupted plan degrades to <see cref="ResolveCast"/>'s own station-voice
    /// default rather than failing the render outright.
    ///
    /// <para>
    /// <b>Entries missing <see cref="AdVoicePlanEntry.Tag"/>/<see cref="AdVoicePlanEntry.VoiceId"/>
    /// are DROPPED, not merely tolerated (T401 review F2).</b> <c>System.Text.Json</c> passes
    /// <see langword="null"/> for a missing constructor-bound reference-type property regardless of
    /// this record's own non-nullable annotation — untrusted jsonb, so this IS reachable from live
    /// data, not a theoretical hole. An unfiltered null <c>Tag</c> would reach
    /// <see cref="ResolveCast"/>'s own <c>Dictionary&lt;string,_&gt;</c> key and throw
    /// <see cref="ArgumentNullException"/> — uncaught here, it propagates to <see cref="RenderAsync"/>'s
    /// outer catch and FAILS the spot outright, exactly the refusal SPEC F161.2's "null plan is
    /// legal" default forbids. Dropping just the bad entries — never refusing the whole plan for one
    /// bad row — degrades gracefully to the station-voice default for any tag no valid entry covers.
    /// </para>
    /// </summary>
    static IReadOnlyList<AdVoicePlanEntry>? ParseVoicePlan(string? voicePlanJson)
    {
        if (string.IsNullOrWhiteSpace(voicePlanJson))
            return null;

        IReadOnlyList<AdVoicePlanEntry>? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize<IReadOnlyList<AdVoicePlanEntry>>(voicePlanJson, AdVoicePlanJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        // entry is not null: a JSON array can itself carry a literal `null` element, which
        // System.Text.Json passes through despite this list's own non-nullable element annotation.
        var valid = deserialized?
            .Where(entry => entry is not null
                && !string.IsNullOrWhiteSpace(entry.Tag)
                && !string.IsNullOrWhiteSpace(entry.VoiceId))
            .ToList();

        return valid is { Count: > 0 } ? valid : null;
    }

    /// <summary>Resolves an optional <c>bed_media_id</c> to a <see cref="BedSpec"/> built from the
    /// referenced row's own path and cue points — never a caller-supplied path (the
    /// <c>SafeSegmentsController.ResolveBedAsync</c> precedent, F27.3's "never trust a raw path"
    /// rule, applied identically here).</summary>
    async Task<(BedSpec? Bed, string? Failure)> ResolveBedAsync(long? bedMediaId, CancellationToken ct)
    {
        if (bedMediaId is null)
            return (null, null);

        var found = await adminLookup.GetByIdWithLibraryAsync(bedMediaId.Value, ct);
        if (found is null)
            return (null, $"render: unknown bed media {bedMediaId.Value}");

        var row = found.Value.Row;
        var (cueIn, cueOut) = ResolveBedCue(bedMediaId.Value, row.CueInSec, row.CueOutSec);
        return (new BedSpec(row.Locator, cueIn, cueOut), null);
    }

    /// <summary>
    /// Mirrors <c>SafeSegmentsController.ResolveBedCue</c>'s identical asymmetric/inverted-cue
    /// discipline: a malformed bed row degrades to no-cue with a WARN rather than throwing out of
    /// <see cref="BedSpec"/>'s own constructor guard.
    ///
    /// <b>Third copy of this exact shape (T401 review F9)</b> — <c>MediaRow.ResolveCue</c>
    /// (GenWave.MediaLibrary) and <c>SafeSegmentsController.ResolveBedCue</c> (GenWave.Host) already
    /// carry the identical asymmetric/inverted-null algorithm. Not extracted here: the three live in
    /// three DIFFERENT layers with no common ancestor below <c>GenWave.Core</c> (MediaLibrary has
    /// Npgsql/L2 concerns Ads must never reference; Host is the outermost layer; Ads references
    /// neither) — a shared pure helper would need to land in <c>GenWave.Core</c> itself, touching
    /// three unrelated projects for a task already this large. The <c>ClampPaging</c> precedent one
    /// project over: a third hand-kept-in-sync copy earns extraction: a FOURTH would not still be a
    /// judgment call.
    /// </summary>
    (double? CueIn, double? CueOut) ResolveBedCue(long bedMediaId, double? cueInSec, double? cueOutSec)
    {
        if (cueInSec.HasValue && cueOutSec.HasValue)
        {
            if (cueInSec.Value >= cueOutSec.Value)
            {
                logger.LogWarning(
                    "Ad bed media {BedMediaId} has inverted cue columns (in={CueIn}, out={CueOut}) — treating as no cue",
                    bedMediaId, cueInSec.Value, cueOutSec.Value);
                return (null, null);
            }
            return (cueInSec, cueOutSec);
        }

        if (!cueInSec.HasValue && !cueOutSec.HasValue)
            return (null, null);

        logger.LogWarning("Ad bed media {BedMediaId} has asymmetric cue columns — treating as no cue", bedMediaId);
        return (null, null);
    }

    async Task<long?> ResolveLibraryIdAsync(CancellationToken ct)
    {
        var library = await libraryRepository.GetByNameAsync(adsOptions.CurrentValue.LibraryName, ct);
        return library?.Id;
    }

    static AuthoredMediaInsert BuildInsert(long libraryId, AudioTags tags, CrosstalkAssemblyResult.Assembled assembled)
    {
        var info = new FileInfo(assembled.Path);
        var format = Path.GetExtension(assembled.Path).TrimStart('.');

        return new AuthoredMediaInsert(
            Path: assembled.Path,
            Format: format,
            LibraryId: libraryId,
            SizeBytes: info.Length,
            Mtime: info.LastWriteTimeUtc,
            Tags: tags,
            Loudness: assembled.Loudness,
            Cue: assembled.Cue,
            Energy: null,
            DurationMs: assembled.DurationMs,
            SampleRate: null,
            Channels: null,
            BitrateKbps: null,
            Kind: ImagingKind.Ad);
    }

    /// <summary>
    /// The ONE place every failure path above turns a reason into an <see cref="AdRenderOutcome"/>
    /// (PLAN T402 review F5a — collapses five identical <c>TryMarkFailedAsync(...) ? Failed :
    /// ClaimConflict</c> ternaries into a single decision with one home): <see cref="AdRenderOutcome.Failed"/>
    /// when the Failed transition actually applied, <see cref="AdRenderOutcome.ClaimConflict"/> when
    /// <see cref="TryMarkFailedAsync"/> reports it did not (the guardian-re-arm race — see that
    /// outcome's own remarks).
    /// </summary>
    async Task<AdRenderOutcome> FailAsync(long spotId, string reason, CancellationToken ct) =>
        await TryMarkFailedAsync(spotId, reason, ct) ? AdRenderOutcome.Failed : AdRenderOutcome.ClaimConflict;

    /// <summary>
    /// The one MarkFailedAsync call site every failure path above funnels through — never throws
    /// itself (the <c>AdsLibrarySeeder</c> "any failure degrades to WARN" posture): a Postgres blip
    /// recording the FAILURE must not itself crash T402's own per-tick worker loop.
    ///
    /// <para>
    /// <b>Returns whether the Failed transition actually applied (PLAN T402, T401 review F1).</b>
    /// <see langword="false"/> covers BOTH a clean "no longer Rendering" report from
    /// <see cref="IAdSpotStore.MarkFailedAsync"/> (the guardian-re-arm race — see
    /// <see cref="AdRenderOutcome.ClaimConflict"/>'s own remarks) AND a genuine exception attempting
    /// the write — either way, <see cref="RenderAsync"/>'s own caller cannot trust the row is
    /// <see cref="AdState.Failed"/>, and <see cref="AdRenderOutcome.ClaimConflict"/> is the honest
    /// "something kept this from landing cleanly" signal for both, distinguishable only by which WARN
    /// line above actually fired.
    /// </para>
    /// </summary>
    async Task<bool> TryMarkFailedAsync(long spotId, string reason, CancellationToken ct)
    {
        // LogSanitize.Strip (T401 review F11, the CodeQL cs/log-forging family): reason can carry an
        // echoed fragment of the stored script (AdScriptParser's own EchoForReason already bounds
        // and strips it, but a THIRD-party CastSegmentFailureReason.FailureDetail funnels an
        // exception .Message through unfiltered) — newline-stripped here so it can never forge
        // additional log entries. The DB-stored fail_reason itself (below) is untouched: an operator
        // reading it in the admin UI should see the true value, not a log-safe one.
        logger.LogWarning("Ad spot {Id} render failed: {Reason}", spotId, LogSanitize.Strip(reason));
        try
        {
            var applied = await spotStore.MarkFailedAsync(spotId, reason, ct);
            if (!applied)
                logger.LogWarning("Ad spot {Id} MarkFailedAsync found it no longer Rendering", spotId);
            return applied;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot {Id} MarkFailedAsync itself failed", spotId);
            return false;
        }
    }
}
