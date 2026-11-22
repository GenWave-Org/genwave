namespace GenWave.Ads.Tests.Fakes;

using GenWave.Tts;

/// <summary>An <see cref="IActiveVoiceEngine"/> that names one engine and has none registered.</summary>
public sealed class FixedActiveVoiceEngine(string name = VoiceEngineProfiles.KokoroName) : IActiveVoiceEngine
{
    public string Name => name;

    public VoiceEngine? Current => null;
}
