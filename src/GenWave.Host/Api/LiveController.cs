using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Host.Playout;

namespace GenWave.Host.Api;

/// <summary>
/// Live-playout endpoints for the single station's Admin UI live page.
/// Served entirely from in-memory state — no engine telnet calls are issued at request time.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class LiveController(
    NowPlayingService nowPlayingService,
    PlayHistoryService historyService) : ControllerBase
{
    /// <summary>
    /// GET /api/now-playing — what is currently on-air.
    ///   503 ProblemDetails — feeder has not completed its first tick yet (cold-start)
    ///   200 { stationId, drain: true } — safe-rotation / drain token is on-air
    ///   200 { stationId, mediaId, title, artist, gainDb, startedAt, durationMs? } — real track on-air
    /// durationMs is null for engine-initiated plays and tts:* patter (SPEC F50.2, F50.6).
    /// </summary>
    [HttpGet("now-playing")]
    public IActionResult GetNowPlaying()
    {
        var stationId = SingleStation.IdString;
        var snapshot = nowPlayingService.GetSnapshot(stationId);

        if (snapshot is null)
        {
            return Problem(
                detail: "The playout feeder is still initialising. Retry in a few seconds.",
                title: "Feeder warming up",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (snapshot.IsDrain)
            return Ok(new { stationId, drain = true });

        return Ok(new
        {
            stationId,
            mediaId = snapshot.MediaId,
            title = snapshot.Title,
            artist = snapshot.Artist,
            gainDb = snapshot.GainDb,
            startedAt = snapshot.StartedAt,
            durationMs = snapshot.DurationMs,
        });
    }

    /// <summary>
    /// GET /api/play-history — play history, newest first; empty array when nothing has aired.
    /// Each entry: { mediaId, title, artist, gainDb, startedAt, endedAt, durationMs? }. durationMs is
    /// null for engine-initiated plays and tts:* patter (SPEC F50.2, F50.6).
    /// </summary>
    [HttpGet("play-history")]
    public IActionResult GetPlayHistory()
    {
        var entries = historyService.GetEntries(SingleStation.IdString);

        var dto = entries.Select(e => new
        {
            mediaId = e.MediaId,
            title = e.Title,
            artist = e.Artist,
            gainDb = e.GainDb,
            startedAt = e.StartedAt,
            endedAt = e.EndedAt,
            durationMs = e.DurationMs,
        });

        return Ok(dto);
    }
}
