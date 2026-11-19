namespace GenWave.Host.Tests;

using GenWave.Tts;

/// <summary>The resolver of a station on the default engine: each speaker's kokoro voice, nothing else.</summary>
public sealed class KokoroVoiceResolver : IVoiceResolver
{
    public static KokoroVoiceResolver Instance { get; } = new();

    public string? Resolve(VoiceMap voices) => voices.VoiceFor(VoiceEngineProfiles.KokoroName);
}
