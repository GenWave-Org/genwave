namespace GenWave.Ads;

using GenWave.Core.Domain;
using GenWave.Tts;

/// <summary>
/// Casts the three voice roles a rendered ad spot needs — ANNOUNCER, VOICE1, VOICE2 — from the live
/// <c>Station:Ads:CastVoices</c> pool, deterministically, once per spot (SPEC F167.1-F167.4; STORY-402;
/// PLAN T415). Pure: no I/O, no logging — <see cref="AdSpotWorker"/> owns turning
/// <see cref="AdCastPick.Outcome"/> into an INFO line and persisting <see cref="AdCastPick.Entries"/>.
///
/// <para>
/// <b>The rule, in order:</b> the effective announcer (<see cref="AdLiveSettings.AnnouncerVoice"/> if
/// non-empty, else <paramref name="stationVoice" /> in <see cref="Pick"/>) is always stripped from the
/// candidate pool before VOICE1/VOICE2 are cast — a spot's second-voice talent must never coincide with
/// its own announcer read (STORY-402 AC2). What's left after that strip decides the branch: two or more
/// candidates casts two distinct voices (<see cref="AdCastOutcome.Cast"/>); exactly one candidate reads
/// both VOICE1 and VOICE2 (<see cref="AdCastOutcome.ThinPool"/>, SPEC F167.2); zero candidates falls back
/// to <paramref name="stationVoice"/> for ALL THREE tags — INCLUDING the announcer, even when
/// <see cref="AdLiveSettings.AnnouncerVoice"/> is itself configured — because SPEC F167.4's own wording is
/// literal about that (<see cref="AdCastOutcome.EmptyPool"/>).
/// </para>
///
/// <para>
/// <b>Not implemented (flagged to /design, PLAN T415 review R12):</b> SPEC F167.3's "unless CastVoices
/// changed since" re-cast clause needs a provenance marker this task's null-only stamp rule doesn't carry
/// — an operator who wants a fresh cast today clears <c>voice_plan</c> on the draft/failed row and lets it
/// re-enter <c>rendering</c>.
/// </para>
/// </summary>
internal static class AdCastPicker
{
    internal const string AnnouncerTag = AdScriptParser.AnnouncerTag;
    internal const string Voice1Tag = "VOICE1";
    internal const string Voice2Tag = "VOICE2";

    public static AdCastPick Pick(AdSpot spot, AdLiveSettings settings, string stationVoice)
    {
        var effectiveAnnouncer = EffectiveAnnouncer(settings, stationVoice);
        var candidates = CastCandidates(settings, effectiveAnnouncer);

        if (candidates.Count == 0)
        {
            return new AdCastPick(
                [
                    new AdVoicePlanEntry(AnnouncerTag, stationVoice),
                    new AdVoicePlanEntry(Voice1Tag, stationVoice),
                    new AdVoicePlanEntry(Voice2Tag, stationVoice),
                ],
                AdCastOutcome.EmptyPool);
        }

        if (candidates.Count == 1)
        {
            var only = candidates[0];
            return new AdCastPick(
                [
                    new AdVoicePlanEntry(AnnouncerTag, effectiveAnnouncer),
                    new AdVoicePlanEntry(Voice1Tag, only),
                    new AdVoicePlanEntry(Voice2Tag, only),
                ],
                AdCastOutcome.ThinPool);
        }

        var rng = new Random(AdDeterministicSeed.FromTerms(
            spot.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            spot.PackSlug ?? AdSourceTokens.ToToken(spot.Source),
            spot.SponsorName));

        var voice1Index = rng.Next(candidates.Count);
        var voice1 = candidates[voice1Index];

        var remainder = candidates.Where((_, index) => index != voice1Index).ToList();
        var voice2 = remainder[rng.Next(remainder.Count)];

        return new AdCastPick(
            [
                new AdVoicePlanEntry(AnnouncerTag, effectiveAnnouncer),
                new AdVoicePlanEntry(Voice1Tag, voice1),
                new AdVoicePlanEntry(Voice2Tag, voice2),
            ],
            AdCastOutcome.Cast);
    }

    /// <summary>
    /// The voice setting values <see cref="Pick"/> could stamp into a spot, each with the setting it came from
    /// (SPEC F219.9: the worker checks exactly these on the active engine). An empty candidate pool stamps the
    /// station voice on every tag; otherwise the announcer (its own setting, else the station voice) and the
    /// remaining cast candidates. With two or more candidates every one is checked, so a single unmapped entry
    /// stops new spots even though a cast might avoid it — deliberate: the booth row names the cast voices
    /// setting and the operator fixes it, rather than spots quietly favouring the mapped voices.
    /// </summary>
    public static IReadOnlyList<(string Setting, string Value)> StampableVoices(AdLiveSettings settings, string stationVoice)
    {
        var effectiveAnnouncer = EffectiveAnnouncer(settings, stationVoice);
        var candidates = CastCandidates(settings, effectiveAnnouncer);
        if (candidates.Count == 0)
            return [(VoiceMap.StationVoiceSetting, stationVoice)];

        var announcerSetting = settings.AnnouncerVoice.Length > 0 ? VoiceMap.AnnouncerVoiceSetting : VoiceMap.StationVoiceSetting;
        return [(announcerSetting, effectiveAnnouncer), .. candidates.Select(voice => (VoiceMap.CastVoicesSetting, voice))];
    }

    static string EffectiveAnnouncer(AdLiveSettings settings, string stationVoice) =>
        settings.AnnouncerVoice.Length > 0 ? settings.AnnouncerVoice : stationVoice;

    static List<string> CastCandidates(AdLiveSettings settings, string effectiveAnnouncer) =>
        settings.CastVoices.Where(voice => !SameSpeaker(voice, effectiveAnnouncer)).ToList();

    /// <summary>
    /// Two voice setting values are the same speaker when they are the same text, or are maps that give one
    /// voice id on a shared engine (a bare id reads as kokoro), SPEC F219.8.
    /// </summary>
    static bool SameSpeaker(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal)
        || (VoiceMap.TryParseSetting(a) is { } first && VoiceMap.TryParseSetting(b) is { } second && first.SharesVoiceWith(second));
}
