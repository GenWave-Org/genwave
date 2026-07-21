namespace GenWave.Tts;

/// <summary>
/// One completed LLM call, exactly as <see cref="LlmCallRing"/> stores it and
/// <c>GET /api/llm-calls</c> (AdminOnly, never persisted) returns it (SPEC F73.1, STORY-196, T41) —
/// the debug lens this whole feature exists for. <see cref="PromptSystem"/>/<see cref="PromptUser"/>/
/// <see cref="Response"/> carry FULL, unredacted text: persona/operator content that belongs here
/// and nowhere else — never a log line (<see cref="LlmCopyWriter"/>'s own WARN deliberately excludes
/// the prompt), never a database row (see <see cref="LlmCallRing"/>'s own remarks).
/// </summary>
/// <param name="Seq">
/// Ring-assigned, monotonically increasing — the newest record has the highest <see cref="Seq"/>.
/// Doubles as a stable row key for the admin UI across polls.
/// </param>
/// <param name="PromptSystem">
/// The system prompt built for this call (persona/soul/quirks/station clock composed in), or
/// <see langword="null"/> if the call faulted before prompt assembly was reached (e.g. a malformed
/// endpoint URI).
/// </param>
/// <param name="PromptUser">The user-turn prompt (segment kind/track/station context) built alongside <see cref="PromptSystem"/>; same null case.</param>
/// <param name="Response">
/// The RAW completion text exactly as the endpoint returned it — BEFORE
/// <c>LlmCopyWriter.CleanCopy</c> hygiene — or <see langword="null"/> for
/// <see cref="LlmCallOutcome.Failed"/>/<see cref="LlmCallOutcome.Timeout"/>, which never received one.
/// </param>
/// <param name="StartedAt">When this call was dispatched (includes any single-flight queueing wait — mirrors <see cref="LlmCopyStatusHolder"/>'s own attemptedAt semantics).</param>
/// <param name="ElapsedMs">Wall-clock duration from <see cref="StartedAt"/> to completion (success or failure).</param>
/// <param name="Outcome">ok/failed/timeout (SPEC F73.1).</param>
/// <param name="StatusDetail">The HTTP status or exception type name for a non-<see cref="LlmCallOutcome.Ok"/> outcome; <see langword="null"/> for Ok.</param>
/// <param name="Mode">The degradation mode active at call time (SPEC F73.1, F69.1) — Normal/Soft/Hard.</param>
public sealed record LlmCallRecord(
    long Seq,
    string? PromptSystem,
    string? PromptUser,
    string? Response,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    LlmCallOutcome Outcome,
    string? StatusDetail,
    DegradationMode Mode);
