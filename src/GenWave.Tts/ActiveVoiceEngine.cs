namespace GenWave.Tts;

using Microsoft.Extensions.Options;

/// <summary>
/// Resolves <c>Tts:Engine</c> against the registry on every read (SPEC F219.3): nothing is cached
/// here, so the live <see cref="IOptionsMonitor{TOptions}"/> value and the live registry decide.
/// <para>
/// Piper-primary rule (SPEC F99.4): when <c>Tts:PiperPrimaryEndpoint</c> is set, Kokoro is absent
/// and <c>Tts:Engine</c> still reads its default <c>kokoro</c>, so that value resolves to
/// <c>piper</c>. Any other engine name is an explicit operator choice and wins.
/// </para>
/// </summary>
public sealed class ActiveVoiceEngine(IVoiceEngineRegistry registry, IOptionsMonitor<TtsOptions> options) : IActiveVoiceEngine
{
    public string Name
    {
        get
        {
            var tts = options.CurrentValue;
            return !string.IsNullOrEmpty(tts.PiperPrimaryEndpoint)
                && string.Equals(tts.Engine, VoiceEngineProfiles.KokoroName, StringComparison.OrdinalIgnoreCase)
                    ? VoiceEngineProfiles.PiperName
                    : tts.Engine;
        }
    }

    public VoiceEngine? Current => registry.Get(Name);
}
