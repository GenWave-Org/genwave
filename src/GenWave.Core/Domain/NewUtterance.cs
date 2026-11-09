namespace GenWave.Core.Domain;

/// <summary>
/// One accepted LLM-written DJ line, as handed to <see cref="Abstractions.IUtteranceStore.RecordAsync"/>
/// (SPEC F218, STORY-532): the raw copy, before the TTS normaliser. <see cref="ShowId"/> is
/// <see langword="null"/> when no schedule segment is on air.
/// </summary>
public sealed record NewUtterance(
    long PersonaId,
    long? ShowId,
    UtteranceKind Kind,
    string SegmentKind,
    ShiftKey ShiftKey,
    string Text,
    bool Fallback);
