using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;
using GenWave.Plugins;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// One cheap aggregate for the Admin UI dashboard (SPEC F28.6, F34.8) — station uptime, catalog
/// health, SafeScope playability, and LLM copy-writer health in a single round-trip, so the
/// dashboard never issues N browse queries just to paint status tiles.
/// </summary>
[ApiController]
[Route("api")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.PlayoutRead)]
public sealed class StatusController(
    IMediaCatalog catalog,
    IMediaRotationSink rotationSink,
    IRotFindingStore rotFindingStore,
    IStationScopeProvider scopeProvider,
    IOptionsMonitor<StationOptions> stationMonitor,
    IOptionsMonitor<LlmOptions> llmMonitor,
    LlmCopyStatusHolder llmStatusHolder,
    LlmCallCauseCounters llmCauseCounters,
    DegradationController degradationController,
    VoiceHealthReader voiceHealthReader,
    IActivePersonaAccessor personaAccessor,
    ProcessStartTime startTime,
    PluginStatusAccessor pluginStatus) : ControllerBase
{
    /// <summary>
    /// GET /api/status — cookie-auth (covered by the deny-by-default fallback policy when
    /// Admin:Password is set, same as every other <c>/api/*</c> controller). Returns:
    /// <c>{ startedAt, catalog: { ready, enriching, failed, unavailable }, safeScope: { libraryIds, playable },
    /// rotation: { playable, neverAired, airedOnce, notAiredDays90, rotationSince },
    /// gardener: { open: { deadFile, nearDuplicate, staleMetadata, shelfDust, unreachable }, total },
    /// llm: { enabled, model, activePersona, lastOutcome, lastAttemptAt, dominantCause, dominantCauseCount, dominantCauseModel },
    /// degradation: { mode, pinned, since, cause },
    /// voice: { engine, degraded, reason, checkedAt },
    /// plugins: [{ name, version, contracts, state, reason? }] }</c>.
    ///
    /// <c>Station:SafeScope:LibraryIds</c> is read via <see cref="IOptionsMonitor{TOptions}.CurrentValue"/>
    /// on every call — not a boot-time snapshot — so a live <c>PUT /api/settings</c> edit
    /// (STORY-058) is reflected on the very next <c>GET</c> with no api restart (the P9
    /// stale-snapshot finding this endpoint must not repeat).
    ///
    /// <c>catalog.*</c> counts and <c>safeScope.playable</c> both come from
    /// <see cref="IMediaCatalog.GetStatusCountsAsync"/> — one grouped query, no engine round-trip.
    ///
    /// <c>llm</c> (SPEC F34.8, STORY-125) is built from config + in-memory state only — NEVER a live
    /// call to the LLM endpoint: <c>enabled</c>/<c>model</c> come from
    /// <see cref="IOptionsMonitor{LlmOptions}.CurrentValue"/> (an empty <c>Llm:Endpoint</c> is
    /// disabled, SPEC F34.2), <c>lastOutcome</c>/<c>lastAttemptAt</c> come from
    /// <see cref="LlmCopyStatusHolder.Last"/> (null until <see cref="LlmCopyWriter.WriteAsync"/> has
    /// made a real on-air attempt — a preview never records here, T7), and <c>activePersona</c> is the
    /// one persona-store read via <see cref="IActivePersonaAccessor.ResolveAsync"/> (already degrades
    /// to null on any miss, F35.5). This endpoint has no <c>IHttpClientFactory</c>/completions
    /// dependency at all, by construction — an idle station polling this endpoint sends the LLM zero
    /// requests.
    ///
    /// <c>llm.dominantCause</c>/<c>dominantCauseCount</c>/<c>dominantCauseModel</c> (SPEC F139.2,
    /// STORY-353, PLAN T334) are <see cref="LlmCallCauseCounters.DominantFailure"/>'s own read,
    /// restricted to <see cref="LlmCallKind.Copy"/> — the SAME kind <c>lastOutcome</c> above reflects,
    /// so this never names a crosstalk-only cause for a tile that went red over an ordinary segment
    /// miss. All three are <see langword="null"/> together whenever nothing but
    /// <see cref="LlmCallCause.Success"/> (or nothing at all) was recorded for Copy calls in the
    /// rolling 24h window — the Admin UI only renders the line once <c>lastOutcome == "failed"</c>
    /// anyway, so a null here on a green tile is simply unused, never a fault. This rides the SAME
    /// poll as every other <c>llm.*</c> field (no new endpoint, no new poller — the gh-#558 lesson):
    /// <see cref="LlmCallCauseCounters.DominantFailure"/> is an in-memory read over already-aggregated
    /// counters, exactly as cheap as <see cref="DegradationController.Evaluate"/> below.
    ///
    /// <c>degradation</c> (SPEC F69.5, STORY-188) comes from
    /// <see cref="DegradationController.Evaluate"/> — called here, not just read from a cached
    /// field, so a just-applied pin or an elapsed probe cooldown is visible on THIS poll rather than
    /// waiting for the next playout render (see that method's own remarks for why this never
    /// performs I/O). <c>mode</c> is lowercase (<c>"normal"</c>/<c>"soft"</c>/<c>"hard"</c>);
    /// <c>pinned</c> is true while <c>Llm:DegradationPin</c> holds <c>mode</c>; <c>since</c> is when
    /// the current mode was entered; <c>cause</c> is the human-readable reason for it.
    ///
    /// <c>voice</c> (SPEC F99.5, F100.3, STORY-256 AC4, PLAN T149) comes from
    /// <see cref="VoiceHealthReader.Evaluate"/> — the primary voice engine's own cached
    /// <see cref="IDependencyHealth"/> verdict, never a live probe. It answers ONLY "is the engine
    /// down"; <c>degradation</c> above answers "does the DJ have anything to say" — an operator
    /// reading both fields on the same response can always tell the two causes of a quiet DJ apart.
    ///
    /// <c>rotation</c> (SPEC F149.5, STORY-368, PLAN T371) comes from
    /// <see cref="IMediaRotationSink.GetRotationHealthAsync"/>, scoped to
    /// <see cref="IStationScopeProvider.Current"/> — the same live-read-every-call posture every
    /// other admin catalog read in this codebase follows (SPEC F30.1) — never <c>safeScope</c> above,
    /// which answers a different question (is the SAFE loop itself populated). <c>playable</c> is the
    /// dashboard tile's own denominator ("N of playable never aired"); <c>rotationSince</c> is
    /// ISO-8601 (<see cref="DateTimeOffset"/>'s own default JSON shape), <see langword="null"/> only on
    /// a pre-Gardener install whose migration has never run.
    ///
    /// <c>gardener</c> (SPEC F153.9, STORY-374, PLAN T377) is the Gardener tile's own open-findings
    /// aggregate, sourced from <see cref="IRotFindingStore.CountOpenByKindAsync"/> — the SAME
    /// live-read-every-call posture <c>rotation</c> above follows. Every <see cref="RotKind"/> is
    /// always present under <c>gardener.open</c> (<c>0</c> when the dictionary carries no entry for
    /// it — <see cref="IRotFindingStore.CountOpenByKindAsync"/>'s own "absent, not present with a
    /// zero" contract is a store-level micro-optimization this endpoint deliberately does NOT leak
    /// onto the wire, so the Admin UI's own tile never has to special-case a missing key);
    /// <c>gardener.total</c> is the sum across every kind — the tile's own single "N findings need
    /// attention" headline number.
    ///
    /// <c>plugins</c> (SPEC F156.7, STORY-385/386, PLAN T394) is the boot-time plugin loader's own
    /// outcome list, read from <see cref="PluginStatusAccessor"/> — an in-memory snapshot, never a
    /// live re-scan (SPEC F156.1: loading happens once, at boot; a plugin-set change is a restart).
    /// Empty when the plugin door is closed (either boot knob missing) — the SAME empty array whether
    /// the door was never opened or a mount held zero valid plugins, since neither case has anything
    /// to report. <c>name</c>/<c>version</c> are the plugin manifest's own raw text, carried verbatim
    /// (the JSON serializer escapes them; see <c>IGenWavePlugin.Name</c>'s own remarks on why
    /// "verbatim" is deliberate here even though the SAME text is stripped before it ever reaches an
    /// <c>ILogger</c> line or a booth-log row — two different surfaces, two different rules).
    /// <c>state</c> is <c>"loaded"</c> or <c>"skipped"</c> (F156.7's own two-value contract — a
    /// <c>RootUnreadable</c> outcome reports as <c>"skipped"</c> too, with <c>name</c>/<c>version</c>
    /// both null); <c>reason</c> is present only when <c>state</c> is <c>"skipped"</c>, naming the
    /// failed stage plus the already-neutralized detail text.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var safeScopeIds = stationMonitor.CurrentValue.SafeScope.LibraryIds;
        var safeScope = new LibraryScope(safeScopeIds.ToArray());

        var counts = await catalog.GetStatusCountsAsync(safeScope, ct);
        var rotation = await rotationSink.GetRotationHealthAsync(scopeProvider.Current, ct);
        var gardenerOpenCounts = await rotFindingStore.CountOpenByKindAsync(ct);
        var persona = await personaAccessor.ResolveAsync(ct);

        var llmConfig = llmMonitor.CurrentValue;
        var llmEnabled = !string.IsNullOrEmpty(llmConfig.Endpoint);
        var lastAttempt = llmStatusHolder.Last;
        var dominantFailure = llmCauseCounters.DominantFailure(LlmCallKind.Copy);
        var degradation = degradationController.Evaluate();
        var voice = voiceHealthReader.Evaluate();

        return Ok(new
        {
            startedAt = startTime.Value,
            catalog = new
            {
                ready = counts.Ready,
                enriching = counts.Enriching,
                failed = counts.Failed,
                unavailable = counts.Unavailable,
            },
            safeScope = new
            {
                libraryIds = safeScopeIds,
                playable = counts.Playable,
            },
            rotation = new
            {
                playable = rotation.Playable,
                neverAired = rotation.NeverAired,
                airedOnce = rotation.AiredOnce,
                notAiredDays90 = rotation.NotAiredDays90,
                rotationSince = rotation.RotationSince,
            },
            gardener = new
            {
                open = new
                {
                    deadFile = gardenerOpenCounts.GetValueOrDefault(RotKind.DeadFile),
                    nearDuplicate = gardenerOpenCounts.GetValueOrDefault(RotKind.NearDuplicate),
                    staleMetadata = gardenerOpenCounts.GetValueOrDefault(RotKind.StaleMetadata),
                    shelfDust = gardenerOpenCounts.GetValueOrDefault(RotKind.ShelfDust),
                    unreachable = gardenerOpenCounts.GetValueOrDefault(RotKind.Unreachable),
                },
                total = gardenerOpenCounts.Values.Sum(),
            },
            llm = new
            {
                enabled = llmEnabled,
                model = llmEnabled && !string.IsNullOrEmpty(llmConfig.Model) ? llmConfig.Model : null,
                activePersona = persona?.Name,
                lastOutcome = lastAttempt is null
                    ? null
                    : lastAttempt.Outcome == LlmAttemptOutcome.Ok ? "ok" : "failed",
                lastAttemptAt = lastAttempt?.AttemptedAt,
                dominantCause = dominantFailure?.Cause.ToString().ToLowerInvariant(),
                dominantCauseCount = dominantFailure?.Count,
                dominantCauseModel = dominantFailure?.Model,
            },
            degradation = new
            {
                mode = degradation.Mode.ToString().ToLowerInvariant(),
                pinned = degradation.Pinned,
                since = degradation.Since,
                cause = degradation.Cause,
            },
            voice = new
            {
                engine = voice.Engine,
                degraded = voice.Degraded,
                reason = voice.Reason,
                checkedAt = voice.CheckedAt,
            },
            plugins = pluginStatus.Reports.Select(ToPluginDto),
        });
    }

    /// <summary>
    /// Projects one <see cref="PluginLoadReport"/> onto <c>plugins[]</c>'s own wire shape (SPEC
    /// F156.7) — <c>name</c>/<c>version</c> carried verbatim (this method's own doc remarks explain
    /// why), <c>reason</c> combining the failed stage and the already-neutralized detail text, present
    /// only on a skipped outcome.
    /// </summary>
    static object ToPluginDto(PluginLoadReport report) => new
    {
        name = report.Name,
        version = report.Version,
        contracts = report.Contracts,
        state = report.State == PluginLoadState.Loaded ? "loaded" : "skipped",
        reason = report.State == PluginLoadState.Loaded ? null : $"{report.Reason}: {report.Detail}",
    };
}
