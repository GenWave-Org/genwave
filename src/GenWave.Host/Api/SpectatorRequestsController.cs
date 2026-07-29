using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Options;

namespace GenWave.Host.Api;

/// <summary>
/// <c>POST /spectator/api/requests</c> — listener song requests (SPEC F87, STORY-224, PLAN T87).
/// A dedicated controller rather than joining <see cref="SpectatorController"/>: this is the
/// codebase's first public anonymous WRITE endpoint, with its own kill switch
/// (<see cref="RequestsSurfaceAttribute"/>, independent of <see cref="SpectatorSurfaceAttribute"/>'s
/// <c>Station:SpectatorMode</c>) and its own dedicated rate-limiter budget
/// (<see cref="RateLimiterPolicies.Requests"/>, cooldown + daily cap) — a genuinely different
/// trust/throttle contract deserves its own type rather than an odd one out beside four read-only
/// GETs (POLA).
/// <para>
/// Carries the same <see cref="AuthorizationPolicies.Spectator"/> policy every spectator endpoint
/// uses (demands nothing, SPEC F60.2) PLUS <see cref="RequestsSurfaceAttribute"/> so the intake
/// route specifically 404s when <c>Station:Requests:Enabled</c> is off, even while the rest of the
/// spectator surface stays reachable.
/// </para>
/// <para>
/// No-oracle discipline throughout (SPEC F87.1): the 202 body
/// (<see cref="SpectatorRequestAccepted"/>) is byte-identical for every accepted wish — matchable,
/// unmatchable, or gibberish — and this controller never logs, echoes, or persists the wish text
/// anywhere but the one <see cref="IRequestStore.InsertAsync"/> row (F87.7). The booth-log events
/// published below (<see cref="RequestReceived"/>/<see cref="RequestEvicted"/>) structurally carry
/// no wish text either — see their own remarks.
/// </para>
/// <para>
/// <c>requestParseQueue</c> (SPEC F87.4, STORY-225, PLAN T88): a non-blocking
/// <see cref="ChannelWriter{T}.TryWrite"/> nudges <c>RequestParserService</c> to parse the fresh row
/// promptly — a full/backed-up queue just means that row waits for the parser's own startup recovery
/// sweep on the next restart (see <c>RequestParsingServiceCollectionExtensions</c>'s own remarks); an
/// anonymous POST must never wait on parsing.
/// </para>
/// </summary>
[ApiController]
[Route("spectator/api")]
[SpectatorSurface]
[RequestsSurface]
[Authorize(Policy = AuthorizationPolicies.Spectator)]
[EnableRateLimiting(RateLimiterPolicies.Requests)]
public sealed class SpectatorRequestsController(
    IRequestStore requestStore,
    IRequestCatalogProbe catalogProbe,
    IOptionsMonitor<StationOptions> stationMonitor,
    IOptions<RequestsOptions> requestsOptions,
    IStationEventSink events,
    ChannelWriter<long> requestParseQueue,
    ILogger<SpectatorRequestsController> logger) : ControllerBase
{
    /// <summary>
    /// POST /spectator/api/requests — SPEC F87.1-F87.3, F87.8; gh-#131 pickers. Flow: at least one
    /// of wish/genre/mood must survive a trim (all absent ⇒ 400, nothing written); an over-length
    /// wish is 400; a picker value outside the CURRENT published list is 400 fail-closed — the
    /// submitted value is never stored, logged, or echoed back (the ProblemDetails bodies below are
    /// fixed strings by construction). Both picker validations are deterministic membership checks —
    /// NO LLM is ever in this path; a member genre is canonicalized to the catalog list's own casing
    /// before storage. Then the pending row count is checked against <c>Requests:PendingCap</c> and
    /// the oldest pending row is evicted first if the station is already at capacity (F87.3); the
    /// request is inserted with <c>expires_at = now + Station:Requests:WindowMinutes</c> and a
    /// <see cref="RequestReceived"/> narrative event is published. The kill-switch 404 and the
    /// cooldown/daily-cap 429 both happen upstream of this action
    /// (<see cref="RequestsSurfaceAttribute"/> in <see cref="SurfaceGateMiddleware"/>,
    /// <see cref="RateLimiterPolicies.Requests"/> in the pipeline) — this method only ever runs for
    /// an enabled, not-yet-throttled caller.
    /// </summary>
    [HttpPost("requests")]
    [Consumes("application/json")]
    // Reviewer-flagged defense-in-depth: an anonymous write should never buffer Kestrel's ~28MB
    // default before the 140-char wish check gets its say. 8KB fits any legal body with headroom.
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> PostRequest([FromBody] SpectatorRequestSubmission submission, CancellationToken ct)
    {
        var wish = NormalizeField(submission.Wish);
        var genre = NormalizeField(submission.Genre);
        var mood = NormalizeField(submission.Mood);

        if (wish is null && genre is null && mood is null)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request.",
                Detail = "at least one of wish, genre, or mood is required.",
            });
        }

        var maxLength = requestsOptions.Value.WishMaxLength;
        if (wish is not null && wish.Length > maxLength)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid wish.",
                Detail = $"wish must be at most {maxLength} characters.",
            });
        }

        // gh-#131 fail-closed membership checks — CURRENT lists, deterministic, no LLM. The 400
        // bodies are fixed strings: the rejected value is never echoed (nor stored, nor logged).
        if (mood is not null && !MoodVocabulary.Contains(mood))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid mood.",
                Detail = "mood must be one of the published mood options.",
            });
        }

        if (genre is not null)
        {
            var genres = await catalogProbe.ListRequestableGenresAsync(ct);
            var canonical = genres.FirstOrDefault(
                option => string.Equals(option, genre, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid genre.",
                    Detail = "genre must be one of the published genre options.",
                });
            }

            genre = canonical;
        }

        if (await requestStore.CountPendingAsync(ct) >= requestsOptions.Value.PendingCap)
        {
            await requestStore.EvictOldestPendingAsync(ct);
            events.Publish(new RequestEvicted());
        }

        var windowMinutes = stationMonitor.CurrentValue.Requests.WindowMinutes;
        var id = await requestStore.InsertAsync(wish, genre, mood, DateTimeOffset.UtcNow.AddMinutes(windowMinutes), ct);
        events.Publish(new RequestReceived());

        // Prompt-parse nudge (SPEC F87.4, STORY-225, PLAN T88) — see this class's own remarks.
        if (!requestParseQueue.TryWrite(id))
            logger.LogDebug("Wish-parse queue full — request {Id} will be picked up by the next recovery sweep", id);

        return Accepted(new SpectatorRequestAccepted());
    }

    /// <summary>Trim-to-null (gh-#131): null, empty, and whitespace-only all collapse to
    /// <see langword="null"/> so every downstream check sees one shape of "absent".</summary>
    static string? NormalizeField(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
