namespace GenWave.Host.Api;

/// <summary>
/// Response body for <c>POST /api/personas/{slug}/import</c> (SPEC F79.3, F79.4; STORY-209, PLAN T67).
/// <see cref="Warnings"/> is always present (empty, never omitted, mirroring
/// <see cref="Core.Domain.PersonaCard"/>'s own "collections are always written" convention). Two cases
/// populate it today: F79.4's unresolved-voice case, and an out-of-range <c>VoiceSpec.Pace</c> (gh-#483,
/// PLAN T140 follow-up — see <c>PersonaController.PaceWarning</c>) — both named here rather than
/// buried in a log line the operator would never see.
/// </summary>
public sealed record PersonaImportResponse(long Id, string Slug, string Name, IReadOnlyList<string> Warnings);
