namespace GenWave.Host.Api;

/// <summary>
/// Response of <c>POST /api/booth-log/{id}/taste-thumb</c> (SPEC F84.1, F84.5; STORY-215, PLAN T70).
/// <see cref="Weight"/> is the accrued artist rule's weight after this thumb, already clamped to
/// <c>[-1, 1]</c> — <see langword="null"/> when <see cref="AlreadyRecorded"/> is <see langword="true"/>:
/// the idempotent no-op path re-reads nothing (T71 only needs the boolean to render the "already
/// thumbed" state).
/// </summary>
public sealed record TasteThumbResponse(bool AlreadyRecorded, double? Weight);
