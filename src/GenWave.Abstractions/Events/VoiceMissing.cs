namespace GenWave.Core.Events;

/// <summary>
/// A speaker has no voice for the active TTS engine, so its talk was dropped and the music carries on
/// (SPEC F219.9, PLAN T753). <paramref name="Speaker"/> names the DJ, or the setting
/// (<c>Station:Voice</c>) when it is the station voice; <paramref name="PersonaId"/> is the DJ's id when there
/// is one.
/// </summary>
public sealed record VoiceMissing(string Speaker, long? PersonaId = null) : StationEvent;
