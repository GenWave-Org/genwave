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
    static readonly JsonSerializerOptions VoicePlanJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RenderAsync(AdSpot spot, CancellationToken ct)
    {
        try
        {
            await RenderCoreAsync(spot, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot {Id} render failed unexpectedly", spot.Id);
            await TryMarkFailedAsync(spot.Id, $"render: unexpected {ex.GetType().Name}", ct);
        }
    }

    async Task RenderCoreAsync(AdSpot spot, CancellationToken ct)
    {
        // A structural re-parse only (int.MaxValue as the per-line ceiling — the length rule was
        // already enforced at write time; re-checking it here would be re-validation, not rendering).
        var parsed = AdScriptParser.Parse(spot.Script ?? "", int.MaxValue);
        if (parsed is not AdScriptValidationResult.Accepted(var script))
        {
            var reason = parsed is AdScriptValidationResult.Refused refused
                ? refused.Violation.Reason
                : "unparseable script";
            await TryMarkFailedAsync(spot.Id, $"render: stored script no longer parses ({reason})", ct);
            return;
        }

        var (bed, bedFailure) = await ResolveBedAsync(spot.BedMediaId, ct);
        if (bedFailure is not null)
        {
            await TryMarkFailedAsync(spot.Id, bedFailure, ct);
            return;
        }

        var libraryId = await ResolveLibraryIdAsync(ct);
        if (libraryId is null)
        {
            await TryMarkFailedAsync(spot.Id, "render: the ads library does not exist yet", ct);
            return;
        }

        var cast = ResolveCast(spot, script);
        var lines = script.Lines.Select(line => new CastLine(line.Tag, line.Text)).ToList();
        var tags = new AudioTags(stationIdentity.Current.Name, spot.Title);
        var ceilingSeconds = spot.SpotSeconds * (1 + adsOptions.CurrentValue.DurationToleranceRatio);
        var outputDirectory = Path.Combine(locatorRoots.AuthoredRoot, "ads");

        var request = new CastAssemblyRequest(lines, cast, ceilingSeconds, tags, outputDirectory, bed, adsOptions.CurrentValue.BedDuckDb);

        var result = await author.AuthorAsync(
            request,
            buildInsert: assembled => BuildInsert(libraryId.Value, tags, assembled),
            confirmAsync: (mediaId, confirmCt) => spotStore.MarkReadyAsync(spot.Id, mediaId, confirmCt),
            ct);

        if (!result.Succeeded)
            await TryMarkFailedAsync(spot.Id, $"render: {result.FailureReason} — {result.FailureDetail}", ct);
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
            deserialized = JsonSerializer.Deserialize<IReadOnlyList<AdVoicePlanEntry>>(voicePlanJson, VoicePlanJsonOptions);
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

    /// <summary>The one MarkFailedAsync call site every failure path above funnels through — never
    /// throws itself (the <c>AdsLibrarySeeder</c> "any failure degrades to WARN" posture): a Postgres
    /// blip recording the FAILURE must not itself crash T402's own per-tick worker loop.</summary>
    async Task TryMarkFailedAsync(long spotId, string reason, CancellationToken ct)
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
            if (!await spotStore.MarkFailedAsync(spotId, reason, ct))
                logger.LogWarning("Ad spot {Id} MarkFailedAsync found it no longer Rendering", spotId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ad spot {Id} MarkFailedAsync itself failed", spotId);
        }
    }
}
