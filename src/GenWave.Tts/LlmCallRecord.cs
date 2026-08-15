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
/// <param name="PersonaName">
/// gh-#429 — the on-air name of whichever persona was active for this call, via the same
/// card-first-then-legacy-row precedence <see cref="LlmPromptBuilder.ResolveName"/> already applies
/// to the prompt's own self-name-mention line, or <see langword="null"/> when no persona was active
/// (persona-less rendering, or a call that faulted before a persona was even resolved). Personas now
/// author the copy this ring exists to triage, so a row needs to name who wrote it without an admin
/// having to read the system prompt to find out.
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
/// <see cref="LlmCallOutcome.Trimmed"/> (SPEC F123.2-F123.4, STORY-319, PLAN T263) carries the RAW
/// reply here exactly like <see cref="LlmCallOutcome.Ok"/> does — the salvaged (shorter) text that
/// actually aired is never stored on this record at all; it exists only as
/// <c>LlmCopyWriter</c>.<c>CleanCopy</c>'s own return value at render time.
/// </param>
/// <param name="StartedAt">When this call was dispatched (includes any single-flight queueing wait — mirrors <see cref="LlmCopyStatusHolder"/>'s own attemptedAt semantics).</param>
/// <param name="ElapsedMs">Wall-clock duration from <see cref="StartedAt"/> to completion (success or failure).</param>
/// <param name="Outcome">ok/failed/timeout/trimmed/rejected (SPEC F73.1, F123.2-F123.4, F127.4).</param>
/// <param name="StatusDetail">The HTTP status or exception type name for a non-<see cref="LlmCallOutcome.Ok"/> outcome; <see langword="null"/> for Ok and for <see cref="LlmCallOutcome.Trimmed"/> alike — a trim is not a fault, so it carries no fault detail either. Carries the discard reason for <see cref="LlmCallOutcome.Rejected"/> (SPEC F127.4, F127.11).</param>
/// <param name="Mode">The degradation mode active at call time (SPEC F73.1, F69.1) — Normal/Soft/Hard.</param>
/// <param name="Kind">
/// Which generation surface produced this call (SPEC F127.11, PLAN T282) — <see cref="LlmCallKind.Copy"/>
/// for every call <see cref="LlmCopyWriter"/> itself records, <see cref="LlmCallKind.Crosstalk"/> for
/// <see cref="CrosstalkScriptWriter"/>'s own. Defaults to <see cref="LlmCallKind.Copy"/> so
/// <see cref="LlmCallRing.Record"/>'s two pre-existing call sites (inside
/// <see cref="LlmCopyWriter.RequestCleanedCompletionAsync"/>) needed no change at all when this
/// parameter was added.
/// </param>
public sealed record LlmCallRecord(
    long Seq,
    string? PersonaName,
    string? PromptSystem,
    string? PromptUser,
    string? Response,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    LlmCallOutcome Outcome,
    string? StatusDetail,
    DegradationMode Mode,
    LlmCallKind Kind = LlmCallKind.Copy);
