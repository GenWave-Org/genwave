using GenWave.Core.Abstractions;
using GenWave.Tts;

namespace GenWave.Host.Voices;

/// <summary>
/// Default <see cref="IVoiceClipUsageReader"/> (SPEC F219.13): reads every persona card's voice map and the three voice settings
/// (<c>Station:Voice</c>, <c>Station:Ads:AnnouncerVoice</c>, <c>Station:Ads:CastVoices</c>, off the live configuration tree), all
/// through <see cref="VoiceMap"/>. An entry only counts on an engine whose profile clones, because only a cloning engine reads a clip
/// as a voice id; the same id on kokoro is a kokoro voice that happens to share the name.
/// </summary>
public sealed class VoiceClipUsageReader(
    IPersonaStore personaStore,
    IConfiguration configuration,
    IVoiceEngineRegistry engines) : IVoiceClipUsageReader
{
    public async Task<IReadOnlyList<string>> FindUsersAsync(string slug, CancellationToken ct)
    {
        var users = new List<string>();

        var personas = await personaStore.GetAllAsync(ct);
        var cards = await personaStore.GetCardsAsync(ct);
        foreach (var persona in personas)
        {
            if (cards.TryGetValue(persona.Id, out var card)
                && MapUsesClip(VoiceMap.FromEngineVoices(card.VoicesByEngine()), slug))
                users.Add(persona.Name);
        }

        if (VoiceMap.TryParseSetting(configuration[VoiceMap.StationVoiceSetting]) is { } stationVoice && MapUsesClip(stationVoice, slug))
            users.Add("the station voice");

        if (VoiceMap.TryParseSetting(configuration[VoiceMap.AnnouncerVoiceSetting]) is { } announcer && MapUsesClip(announcer, slug))
            users.Add("the ad announcer voice");

        if (VoiceMap.TryParseCastSetting(configuration[VoiceMap.CastVoicesSetting]) is { } cast && cast.Any(speaker => MapUsesClip(speaker, slug)))
            users.Add("the ad cast voices");

        return users;
    }

    bool MapUsesClip(VoiceMap map, string slug) =>
        map.Engines.Any(engine => EngineClones(engine) && string.Equals(map.VoiceFor(engine), slug, StringComparison.Ordinal));

    /// <summary>A configured engine's own profile wins; an engine the registry does not hold falls back to the shipped profiles.</summary>
    bool EngineClones(string engineName) =>
        (engines.Get(engineName)?.Profile
            ?? VoiceEngineProfiles.All.FirstOrDefault(p => string.Equals(p.Name, engineName, StringComparison.OrdinalIgnoreCase)))
        is { Clones: true };
}
