using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side implementation of <see cref="IStationDefaultEnvelopeSource"/> (SPEC F81.3, F91.4;
/// STORY-212, STORY-241, PLAN T120): wraps <see cref="IOptionsMonitor{TOptions}"/> so both
/// <c>GenWave.Orchestration.ScheduleResolver</c> (a grid gap, or a segment's NULL envelope field) and
/// <c>GenWave.Orchestration.ScheduleEnvelopeProvider</c> (the process boot window, before the first
/// schedule resolve) share ONE construction of "the station-default envelope from
/// <c>Station:Envelope:*</c>".
///
/// <para>
/// Was <c>OptionsMonitorEnvelopeProvider</c> — v1's ONLY <see cref="IEnvelopeProvider"/> binding,
/// before the F91 format clock existed. <c>GenWave.Orchestration.ScheduleEnvelopeProvider</c> is the
/// <see cref="IEnvelopeProvider"/> binding now; this type keeps the exact same <see cref="Current"/>
/// construction, just re-homed onto the narrower seam that construction always really was.
/// </para>
///
/// Builds a new <see cref="SegmentEnvelope"/> from <see cref="StationEnvelopeOptions"/> on every call
/// — nothing is cached here — <see cref="IOptionsMonitor{T}.CurrentValue"/> already is the cache.
/// <see cref="SegmentEnvelope.StartsAt"/>/<see cref="SegmentEnvelope.EndsAt"/> are always the full day
/// (the station-default envelope is never itself segment-shaped; only a real schedule row narrows
/// them, via <c>ScheduleResolver.BuildSegmentEnvelope</c>).
/// </summary>
sealed class OptionsMonitorStationDefaultEnvelopeSource(
    IOptionsMonitor<StationOptions> stationMonitor,
    ILogger<OptionsMonitorStationDefaultEnvelopeSource> logger) : IStationDefaultEnvelopeSource
{
    public SegmentEnvelope Current
    {
        get
        {
            var envelope = stationMonitor.CurrentValue.Envelope;
            return new SegmentEnvelope(
                TimeOnly.MinValue,
                TimeOnly.MaxValue,
                ParseGenres(envelope.Genres, logger),
                new EnergyRange(envelope.EnergyMin, envelope.EnergyMax));
        }
    }

    /// <summary>
    /// Parses <see cref="StationEnvelopeOptions.Genres"/>'s raw JSON array (same opaque-string-kind
    /// idiom <c>Tts:Corrections</c> uses — see that class's own remarks). Null, blank, or malformed
    /// JSON all degrade to "no genre constraint" (SPEC F81.1's empty-Genres-means-all-genres
    /// contract) with one WARN on malformed input — operator-authored data must never take
    /// selection down. The live-edit path (<c>SettingValidator.IsValidGenresArray</c>) already
    /// rejects this shape going forward; this only guards a boot-time appsettings/env typo.
    /// </summary>
    static IReadOnlyList<string> ParseGenres(string? raw, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "Station:Envelope:Genres could not be parsed; treating as no genre constraint until fixed");
            return [];
        }
    }
}
