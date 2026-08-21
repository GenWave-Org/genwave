using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// The LLM call inspector's admin-only read endpoint (SPEC F73.1-F73.2, F139.2, STORY-196/353, T41,
/// T334) — a debug lens, NOT an audit trail: every entry <see cref="LlmCallRing"/> currently holds
/// (the last ~<see cref="LlmOptions.CallRingCapacity"/> calls — on-air renders, Soft-cadence
/// attempts, and operator previews alike), newest first, full prompt/response text included, plus the
/// <see cref="LlmCallCauseCounters"/> rolling 24h summary alongside it (see
/// <see cref="LlmCallsResponseDto"/>'s own remarks for why one wrapped response, not a second
/// endpoint). Never persisted (F73.3, F139.3): this endpoint only ever reads the two in-memory
/// singletons <see cref="GenWave.Tts.LlmCallRecorder"/> writes into together — nothing here ever
/// touches disk or a database, so a process restart clears both with no explicit "clear" step to
/// forget. Deny-by-default like every other admin route: no <see cref="SpectatorSurfaceAttribute"/>,
/// no public reachability (F73.2).
/// </summary>
[ApiController]
[Route("api/llm-calls")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.PlayoutRead)]
public sealed class LlmCallsController(LlmCallRing ring, LlmCallCauseCounters causeCounters) : ControllerBase
{
    /// <summary>
    /// GET /api/llm-calls — every call the ring currently holds, newest first (SPEC F73.1), plus the
    /// F139.2 rolling 24h cause counters (SPEC F139.2, PLAN T334) in the SAME response
    /// (<see cref="LlmCallsResponseDto"/>). No paging: the ring is capped at
    /// <see cref="LlmOptions.CallRingCapacity"/> (~50) by construction, and the counters are already a
    /// small, pre-aggregated read (<see cref="LlmCallCauseCounters.Snapshot"/>) — the whole thing stays
    /// a single, small round-trip.
    /// </summary>
    [HttpGet]
    public IActionResult List()
    {
        var calls = ring.Snapshot().Select(ToDto).ToList();
        var causeSummary = causeCounters.Snapshot().Select(ToSummaryDto).ToList();
        return Ok(new LlmCallsResponseDto(calls, causeSummary));
    }

    static LlmCallDto ToDto(LlmCallRecord record) => new(
        record.Seq,
        record.PersonaName,
        record.StartedAt,
        record.ElapsedMs,
        record.Outcome.ToString().ToLowerInvariant(),
        record.StatusDetail,
        record.Mode.ToString().ToLowerInvariant(),
        record.PromptSystem,
        record.PromptUser,
        record.Response,
        (record.PromptSystem?.Length ?? 0) + (record.PromptUser?.Length ?? 0),
        record.Response?.Length ?? 0,
        record.Kind.ToString().ToLowerInvariant(),
        record.Cause.ToString().ToLowerInvariant(),
        record.Model);

    static LlmCallCauseSummaryDto ToSummaryDto(LlmCallCauseCount count) => new(
        count.Cause.ToString().ToLowerInvariant(),
        count.Model,
        count.Kind.ToString().ToLowerInvariant(),
        count.Count);
}
