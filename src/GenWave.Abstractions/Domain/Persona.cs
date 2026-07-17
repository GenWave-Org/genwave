namespace GenWave.Core.Domain;

/// <summary>
/// A DJ persona row (SPEC F35.1, STORY-118): the backstory/style/voice profile a future
/// orchestrator task blends into TTS patter. <see cref="Voice"/> of <c>""</c> is a deliberate
/// sentinel meaning "use the station's own default voice" (<c>Station:Voice</c>), not "unset".
/// </summary>
public sealed record Persona(
    long Id,
    string Name,
    string Backstory,
    string Style,
    string Voice,
    DateTime CreatedAt,
    DateTime UpdatedAt);
