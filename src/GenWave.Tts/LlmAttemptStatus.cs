namespace GenWave.Tts;

/// <summary>
/// A point-in-time snapshot of the most recent <see cref="LlmCopyWriter"/> completion attempt
/// (SPEC F34.8). Held by <see cref="LlmCopyStatusHolder"/> for <c>GET /api/status</c> to read.
/// </summary>
/// <param name="Outcome">Whether the attempt produced usable copy or fell back to the template.</param>
/// <param name="AttemptedAt">Wall-clock time the attempt completed.</param>
public sealed record LlmAttemptStatus(LlmAttemptOutcome Outcome, DateTimeOffset AttemptedAt);
