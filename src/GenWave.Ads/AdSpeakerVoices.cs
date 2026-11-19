namespace GenWave.Ads;

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.Logging;
using GenWave.Tts;
using Microsoft.Extensions.Logging;

/// <summary>
/// The one place an ad spot's speakers get a voice for the engine speaking right now (SPEC F219.8, F219.9; PLAN
/// T753b). A spot's stored voice plan holds voice setting values (a bare id, or an engine map), never a resolved
/// id, so switching <c>Tts:Engine</c> re-voices every spot at its next render without re-casting. A speaker with
/// no voice on the active engine says nothing: <see cref="AdCastResolution.MissingSettings"/> names the setting,
/// and <see cref="ReportMissing"/> writes the <c>voice-missing</c> booth row for it.
/// </summary>
/// <remarks>Public because <see cref="AdRenderService"/>'s public constructor takes it (CS0051).</remarks>
public sealed class AdSpeakerVoices(
    IVoiceResolver resolver,
    IStationIdentityProvider stationIdentity,
    ILogger<AdSpeakerVoices> logger,
    IStationEventSink? events = null)
{
    readonly IStationEventSink events = events ?? NoOpStationEventSink.Instance;

    string? lastConfiguredReport;

    /// <summary>
    /// Resolves each of <paramref name="scriptTags"/>: the plan entry's voice when the plan names one, else the
    /// station voice (the unplanned-tag fallback). A blank resolved voice means the engine's own default voice,
    /// as before engines were a list.
    /// </summary>
    public AdCastResolution Resolve(
        AdSpot spot, IReadOnlyList<string> scriptTags, IReadOnlyDictionary<string, AdVoicePlanEntry> planByTag,
        AdLiveSettings live)
    {
        var cast = new List<CastMember>();
        var missing = new List<string>();
        foreach (var tag in scriptTags)
        {
            var planned = planByTag.GetValueOrDefault(tag);
            var voiceId = planned is null ? StationVoiceOrNull() : resolver.ResolveSetting(planned.VoiceId);
            if (voiceId is null)
            {
                var name = NameFor(spot, tag, planned, live);
                if (!missing.Contains(name, StringComparer.Ordinal))
                    missing.Add(name);
                continue;
            }

            cast.Add(new CastMember(tag, new VoiceSpec(Engine: "", voiceId, planned?.Pace ?? 1.0, Language: "en")));
        }

        return new AdCastResolution(cast, missing);
    }

    /// <summary>One <c>voice-missing</c> booth row and log line per name (SPEC F219.9).</summary>
    public void ReportMissing(AdSpot spot, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            logger.LogWarning(
                "No voice for the active engine in {Setting}; ad spot {Id} does not render (SPEC F219.9)",
                LogSanitize.Strip(name), spot.Id);
            events.Publish(new VoiceMissing(name));
        }
    }

    /// <summary>
    /// True when every ad voice setting in use resolves on the active engine. When not, the settings that do not
    /// are reported (booth row + log) once per change of engine or settings, never once per worker tick, so the
    /// caller can skip generating a spot that could never render.
    /// </summary>
    public bool ConfiguredVoicesResolve(AdLiveSettings live)
    {
        var unresolved = new List<string>();
        foreach (var (setting, value) in AdCastPicker.StampableVoices(live, live.StationVoice))
        {
            if (resolver.ResolveSetting(value) is null && !unresolved.Contains(setting, StringComparer.Ordinal))
                unresolved.Add(setting);
        }

        if (unresolved.Count == 0)
        {
            lastConfiguredReport = null;
            return true;
        }

        var report = $"{live.ActiveEngine}|{string.Join('|', unresolved)}|{live.StationVoice}|{live.AnnouncerVoice}|{string.Join('|', live.CastVoices)}";
        if (!string.Equals(report, lastConfiguredReport, StringComparison.Ordinal))
        {
            lastConfiguredReport = report;
            foreach (var setting in unresolved)
            {
                logger.LogWarning(
                    "No voice for the active engine in {Setting}; no ad spot is written until it has one (SPEC F219.9)",
                    LogSanitize.Strip(setting));
                events.Publish(new VoiceMissing(setting));
            }
        }

        return false;
    }

    string? StationVoiceOrNull() => stationIdentity.Current is { VoiceMissing: false } station ? station.Voice : null;

    /// <summary>
    /// What a missing voice is named after. A plan is written once, so its voice may not come from any setting
    /// today (a spot stamped before engines were a list, or a voice pinned in the ad editor): a setting is named
    /// only when the plan's voice IS that setting's current value (the announcer's own setting, else the station
    /// voice it falls back to; the cast pool, else the station voice). Otherwise the spot's own voice cast is
    /// named, since editing a setting would not change it. An existing failed spot keeps its stamped values after
    /// the operator fixes a setting (re-cast is not implemented), so its next failure names the spot's own voice
    /// cast. A tag the plan never named falls back to the station voice.
    /// </summary>
    static string NameFor(AdSpot spot, string tag, AdVoicePlanEntry? planned, AdLiveSettings live)
    {
        if (planned is null)
            return VoiceMap.StationVoiceSetting;

        var voice = planned.VoiceId.Trim();
        var source = tag == AdCastPicker.AnnouncerTag
            ? live.AnnouncerVoice.Length > 0 && live.AnnouncerVoice == voice ? VoiceMap.AnnouncerVoiceSetting : null
            : live.CastVoices.Contains(voice, StringComparer.Ordinal) ? VoiceMap.CastVoicesSetting : null;
        if (source is null && live.StationVoice == voice)
            source = VoiceMap.StationVoiceSetting;

        return source ?? $"ad spot {spot.Id} voice cast";
    }
}
