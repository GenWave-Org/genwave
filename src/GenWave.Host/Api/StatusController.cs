using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Options;
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
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class StatusController(
    IMediaCatalog catalog,
    IOptionsMonitor<StationOptions> stationMonitor,
    IOptionsMonitor<LlmOptions> llmMonitor,
    LlmCopyStatusHolder llmStatusHolder,
    IActivePersonaAccessor personaAccessor,
    ProcessStartTime startTime) : ControllerBase
{
    /// <summary>
    /// GET /api/status — cookie-auth (covered by the deny-by-default fallback policy when
    /// Admin:Password is set, same as every other <c>/api/*</c> controller). Returns:
    /// <c>{ startedAt, catalog: { ready, enriching, failed, unavailable }, safeScope: { libraryIds, playable },
    /// llm: { enabled, model, activePersona, lastOutcome, lastAttemptAt } }</c>.
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
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var safeScopeIds = stationMonitor.CurrentValue.SafeScope.LibraryIds;
        var safeScope = new LibraryScope(safeScopeIds.ToArray());

        var counts = await catalog.GetStatusCountsAsync(safeScope, ct);
        var persona = await personaAccessor.ResolveAsync(ct);

        var llmConfig = llmMonitor.CurrentValue;
        var llmEnabled = !string.IsNullOrEmpty(llmConfig.Endpoint);
        var lastAttempt = llmStatusHolder.Last;

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
            llm = new
            {
                enabled = llmEnabled,
                model = llmEnabled && !string.IsNullOrEmpty(llmConfig.Model) ? llmConfig.Model : null,
                activePersona = persona?.Name,
                lastOutcome = lastAttempt is null
                    ? null
                    : lastAttempt.Outcome == LlmAttemptOutcome.Ok ? "ok" : "failed",
                lastAttemptAt = lastAttempt?.AttemptedAt,
            },
        });
    }
}
