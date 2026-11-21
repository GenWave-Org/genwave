namespace GenWave.Tts;

/// <summary>
/// Resolves against <see cref="IActiveVoiceEngine.Name"/> on every call; nothing is cached. An engine whose profile cannot
/// choose a voice per request (piper) never silences a speaker: it takes its own entry, else the kokoro one, else
/// any. Every other engine needs its own entry, or the talk drops.
/// </summary>
public sealed class ActiveEngineVoiceResolver(IActiveVoiceEngine active) : IVoiceResolver
{
    public string? Resolve(VoiceMap voices)
    {
        var own = voices.VoiceFor(active.Name);
        return own is not null || active.Current?.Profile is not { PicksVoice: false }
            ? own
            : voices.VoiceFor(VoiceEngineProfiles.KokoroName) ?? voices.FirstVoice();
    }
}
