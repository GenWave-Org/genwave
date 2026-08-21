namespace GenWave.Host.Api;

/// <summary>
/// Response shape for <c>GET /api/llm-calls</c> (SPEC F73.1-F73.2, F139.2, STORY-196/353, PLAN T334)
/// — mirrors <see cref="BoothLogPageDto"/>'s own established "array plus metadata, one object" shape
/// one controller over, rather than inventing a second one: <see cref="Calls"/> is exactly what the
/// endpoint returned bare before this task (STORY-196), newest first, capped at ring size;
/// <see cref="CauseSummary"/> is the F139.2 rolling 24h counters riding the SAME round trip so the
/// admin llm-calls page never issues a second request just to explain the rows it already has (the
/// gh-#558 "no new chatty poller" lesson applies here too, even off the dashboard's own poll cadence:
/// one request beats two regardless of which page is asking). A bare JSON array has no room for a
/// second, named field alongside it — that is the whole reason this wraps rather than keeping the
/// pre-T334 shape, the "no new endpoint unless the existing shape genuinely can't carry it" call PLAN
/// T334 asked this controller to make.
/// </summary>
public sealed record LlmCallsResponseDto(
    IReadOnlyList<LlmCallDto> Calls,
    IReadOnlyList<LlmCallCauseSummaryDto> CauseSummary);
