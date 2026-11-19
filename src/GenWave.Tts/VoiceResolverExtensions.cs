namespace GenWave.Tts;

using GenWave.Core.Domain;

/// <summary>
/// The two shapes every render site resolves a voice from: a setting (<c>Station:Voice</c>) and a persona card
/// (SPEC F219.7/F219.8). Both go through the one <see cref="IVoiceResolver"/>, so a site never re-derives the
/// engine rule.
/// </summary>
public static class VoiceResolverExtensions
{
    /// <summary>
    /// The voice id a voice setting gives on the active engine. A blank setting is <c>""</c> (the engine's own
    /// default voice, as before engines were a list); a bare id or an engine map goes through the resolver.
    /// Null means the setting names voices for other engines only (or is malformed): the talk drops.
    /// </summary>
    public static string? ResolveSetting(this IVoiceResolver resolver, string? setting) =>
        string.IsNullOrWhiteSpace(setting)
            ? string.Empty
            : VoiceMap.TryParseSetting(setting) is { } map ? resolver.Resolve(map) : null;

    /// <summary>
    /// The voice id a persona card gives on the active engine. <c>""</c> means the card has no voice of its own
    /// (every entry blank: the station voice applies); null means it names voices for other engines only (the
    /// talk drops).
    /// </summary>
    public static string? ResolveCard(this IVoiceResolver resolver, PersonaCard card)
    {
        var own = card.VoicesByEngine()
            .Where(entry => !string.IsNullOrEmpty(entry.Value))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        return own.Count == 0 ? string.Empty : resolver.Resolve(VoiceMap.FromEngineVoices(own));
    }

    /// <summary>
    /// True when the active engine is the engine of the card's own lockstep voice (<c>Voice.Engine</c>, <c>""</c>
    /// reading as kokoro). Decided by engine, never by comparing voice id strings (F219.11): two engines may share an
    /// id. Only then is the persona row's flat voice (the card's own voice, install-checked at import) the voice to
    /// render; on any other engine the card's per-engine map decides.
    /// </summary>
    public static bool ActiveEngineOwnsCardVoice(this IVoiceResolver resolver, PersonaCard card) =>
        resolver.Resolve(VoiceMap.FromEngineVoices(new Dictionary<string, string> { [card.Voice.Engine] = card.Voice.VoiceId })) is not null;

    /// <summary>
    /// The voice a persona renders with on the active engine given its row's flat voice: the row voice when the
    /// active engine owns the card's voice, else the card map's voice (<c>""</c> = the station voice, null = none).
    /// </summary>
    public static string? ResolveCardOrRow(this IVoiceResolver resolver, PersonaCard card, string rowVoice) =>
        resolver.ActiveEngineOwnsCardVoice(card) ? rowVoice : resolver.ResolveCard(card);
}
