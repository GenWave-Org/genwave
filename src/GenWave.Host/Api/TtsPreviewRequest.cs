namespace GenWave.Host.Api;

/// <summary>
/// Request body for <c>POST /api/tts/preview</c> (SPEC F35.6, F126.1). <see cref="Voice"/> defaults
/// to <c>Station:Voice</c> when omitted — the Admin UI is expected to pass an explicit voice most of
/// the time, but a bare-text preview still works.
///
/// <see cref="CandidateRules"/> (PLAN T274, STORY-323 AC2/AC4) is an optional set of unsaved
/// pronunciation rules the operator is authoring — layered OVER the resolved station∪persona merge
/// for THIS render only (never persisted, never affects any other request). Omitted or empty means
/// "audition the resolved merge as-is", the common case. Each entry is validated before any render
/// runs; a malformed one 400s naming the offending field rather than silently degrading, mirroring
/// <c>PronunciationsController</c>'s own write-path posture.
/// </summary>
public sealed record TtsPreviewRequest(
    string? Text, string? Voice, IReadOnlyList<TtsPreviewCandidateRule>? CandidateRules = null);
