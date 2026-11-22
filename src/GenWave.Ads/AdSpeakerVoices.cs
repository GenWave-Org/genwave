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
/// id, so switching <c>Tts:Engine</c> re-voices every spot at its next render. A stored voice with none on the
/// active engine (a spot stamped before engines were a list) is re-cast from the setting that holds it (PLAN T773a).
/// A speaker still with no voice on the active engine says nothing: <see cref="AdCastResolution.MissingSettings"/>
/// names the setting, and <see cref="ReportMissing"/> writes the <c>voice-missing</c> booth row for it.
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
            var holder = voiceId is null && planned is not null ? HoldingSetting(tag, planned, live) : null;
            if (holder is not null)
                voiceId = resolver.ResolveSetting(holder.Value);
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

    /// <summary>A current voice setting: its name (what a voice-missing row names) and its value.</summary>
    sealed record HoldingVoice(string Setting, string Value);

    /// <summary>
    /// The first current setting that holds a stored voice with none on the active engine (a spot stamped before
    /// engines were a list holds a bare kokoro id; F219.8): the announcer setting (ANNOUNCER tag only), each cast
    /// entry in order, then the station voice, the first whose value is the same speaker as the stored one. That
    /// setting decides: it re-casts the spot onto the active engine, or, with no voice there, is the setting a
    /// voice-missing row names. Render time only: the stored plan is never rewritten, so switching back renders the
    /// stored ids (F219.11). Null when no setting holds the voice.
    /// </summary>
    static HoldingVoice? HoldingSetting(string tag, AdVoicePlanEntry planned, AdLiveSettings live)
    {
        var stored = planned.VoiceId.Trim();
        var candidates = new List<HoldingVoice>();
        if (tag == AdCastPicker.AnnouncerTag)
            candidates.Add(new HoldingVoice(VoiceMap.AnnouncerVoiceSetting, live.AnnouncerVoice));
        candidates.AddRange(live.CastVoices.Select(voice => new HoldingVoice(VoiceMap.CastVoicesSetting, voice)));
        candidates.Add(new HoldingVoice(VoiceMap.StationVoiceSetting, live.StationVoice));
        return candidates.FirstOrDefault(
            candidate => candidate.Value.Length > 0 && AdCastPicker.SameSpeaker(candidate.Value, stored));
    }

    string? StationVoiceOrNull() => stationIdentity.Current is { VoiceMissing: false } station ? station.Voice : null;

    /// <summary>
    /// What a missing voice is named after: the setting that holds the plan's voice (<see cref="HoldingSetting"/>),
    /// so editing that setting can fix it. A plan is written once, so its voice may come from no setting today (a
    /// spot stamped before engines were a list, or a voice pinned in the ad editor); then the spot's own voice cast
    /// is named, since editing a setting would not change it. A tag the plan never named falls back to the station
    /// voice.
    /// </summary>
    static string NameFor(AdSpot spot, string tag, AdVoicePlanEntry? planned, AdLiveSettings live) =>
        planned is null
            ? VoiceMap.StationVoiceSetting
            : HoldingSetting(tag, planned, live)?.Setting ?? $"ad spot {spot.Id} voice cast";
}
