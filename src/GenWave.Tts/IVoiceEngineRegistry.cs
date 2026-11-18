namespace GenWave.Tts;

/// <summary>The configured voice engines (SPEC F219.1), built and validated at boot.</summary>
public interface IVoiceEngineRegistry
{
    IReadOnlyList<VoiceEngine> All { get; }

    /// <summary>The engine named <paramref name="name"/> (case-insensitive), or null when none is configured.</summary>
    VoiceEngine? Get(string name);
}
