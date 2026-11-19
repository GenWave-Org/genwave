using GenWave.Core.Domain;
using GenWave.Tts;

namespace GenWave.Host.Api;

/// <summary>The engine a persona card's own voice belongs to: the legacy empty engine name means kokoro (as VoiceMap does).</summary>
internal static class PersonaCardEngine
{
    public static string Of(PersonaCard card) =>
        card.Voice.Engine.Length == 0 ? VoiceEngineProfiles.KokoroName : card.Voice.Engine;
}
