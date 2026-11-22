namespace GenWave.Ads.Tests.Fakes;

using GenWave.Core.Abstractions;
using GenWave.Tts;

/// <summary>Builds the voice collaborators the ad specs share: a station on the default (kokoro) engine unless a spec names another.</summary>
public static class AdTestVoices
{
    public static AdSpeakerVoices SpeakersFor(
        IStationIdentityProvider stationIdentity, string engine = VoiceEngineProfiles.KokoroName,
        IStationEventSink? events = null) =>
        new(new ActiveEngineVoiceResolver(new FixedActiveVoiceEngine(engine)), stationIdentity,
            new NoOpLogger<AdSpeakerVoices>(), events);

    public static FixedActiveVoiceEngine Kokoro { get; } = new();
}
