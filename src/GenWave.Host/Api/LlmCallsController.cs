using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>
/// The LLM call inspector's admin-only read endpoint (SPEC F73.1-F73.2, STORY-196, T41) — a debug
/// lens, NOT an audit trail: every entry <see cref="LlmCallRing"/> currently holds (the last
/// ~<see cref="LlmOptions.CallRingCapacity"/> calls — on-air renders, Soft-cadence attempts, and
/// operator previews alike), newest first, full prompt/response text included. Never persisted
/// (F73.3): this endpoint only ever reads the one in-memory singleton
/// <c>LlmCopyWriter.RequestCompletionAsync</c> records into — nothing here ever touches disk or a
/// database, so a process restart clears it with no explicit "clear" step to forget. Deny-by-default
/// like every other admin route: no <see cref="SpectatorSurfaceAttribute"/>, no public reachability
/// (F73.2).
/// </summary>
[ApiController]
[Route("api/llm-calls")]
[AdminSurface]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class LlmCallsController(LlmCallRing ring) : ControllerBase
{
    /// <summary>
    /// GET /api/llm-calls — every call the ring currently holds, newest first (SPEC F73.1). No
    /// paging: the ring is capped at <see cref="LlmOptions.CallRingCapacity"/> (~50) by construction,
    /// so the whole thing is always a small, single-round-trip response.
    /// </summary>
    [HttpGet]
    public IActionResult List()
    {
        var rows = ring.Snapshot().Select(ToDto).ToList();
        return Ok(rows);
    }

    static LlmCallDto ToDto(LlmCallRecord record) => new(
        record.Seq,
        record.StartedAt,
        record.ElapsedMs,
        record.Outcome.ToString().ToLowerInvariant(),
        record.StatusDetail,
        record.Mode.ToString().ToLowerInvariant(),
        record.PromptSystem,
        record.PromptUser,
        record.Response,
        (record.PromptSystem?.Length ?? 0) + (record.PromptUser?.Length ?? 0),
        record.Response?.Length ?? 0);
}
