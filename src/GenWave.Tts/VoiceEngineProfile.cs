namespace GenWave.Tts;

/// <summary>
/// What a voice engine can do (SPEC F219.1, ARCHITECTURE "Any voice engine"). Data only — the
/// renderer (T746) reads these flags to shape a request; nothing here talks to an engine.
/// <see cref="MaxChars"/> null means no per-line limit (no engine enforces one today).
/// </summary>
public sealed record VoiceEngineProfile(
    string Name,
    bool Speed,
    bool Ipa,
    bool PauseTag,
    bool Clones,
    IReadOnlyList<string> SoundTags,
    int? MaxChars);
