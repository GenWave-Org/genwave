namespace GenWave.Host.Api;

/// <summary>
/// One row of <c>GET /api/llm-calls</c>' <c>causeSummary</c> array (SPEC F139.2, STORY-353, PLAN
/// T334) — a direct projection of <see cref="GenWave.Tts.LlmCallCauseCount"/>: how many calls landed
/// on <see cref="Cause"/> for <see cref="Model"/>/<see cref="Kind"/> within the rolling 24h window
/// <see cref="GenWave.Tts.LlmCallCauseCounters"/> tracks. <see cref="Cause"/>/<see cref="Kind"/> are
/// lowercased the same way every other enum-backed field on this endpoint already is (SPEC F73.1's
/// <c>status</c>/<c>mode</c>, F127.11's <c>kind</c> on <see cref="LlmCallDto"/>) — this is the
/// admin-only debug lens's own aggregate, not a public metrics surface, so plain strings over a
/// closed union keep the wire tolerant of a taxonomy that may still grow (SPEC F139.1's own history:
/// eight values already, up from seven at T330 review).
/// </summary>
public sealed record LlmCallCauseSummaryDto(string Cause, string Model, string Kind, int Count);
