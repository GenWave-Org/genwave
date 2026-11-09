namespace GenWave.Core.Domain;

/// <summary>A stored DJ line read back from <c>station.utterance</c> (SPEC F218, STORY-532).
/// <see cref="AiredAt"/> is <see langword="null"/> until the engine reports the file started.</summary>
public sealed record UtteranceEntry(
    long Id,
    long PersonaId,
    long? ShowId,
    UtteranceKind Kind,
    string SegmentKind,
    ShiftKey ShiftKey,
    string Text,
    bool Fallback,
    DateTimeOffset WrittenAt,
    DateTimeOffset? AiredAt);
