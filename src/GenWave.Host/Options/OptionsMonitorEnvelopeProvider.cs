using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IEnvelopeProvider"/> seam (SPEC F81.3, STORY-212): wraps
/// <see cref="IOptionsMonitor{TOptions}"/> so the Orchestrator's envelope-aware pick reads the SAME
/// live value <c>PUT /api/settings</c> writes (mirrors <see cref="OptionsMonitorBoundaryBiasProvider"/>).
///
/// Builds a new <see cref="SegmentEnvelope"/> from <see cref="StationEnvelopeOptions"/> on every call
/// — nothing is cached here — <see cref="IOptionsMonitor{T}.CurrentValue"/> already is the cache.
/// <see cref="SegmentEnvelope.StartsAt"/>/<see cref="SegmentEnvelope.EndsAt"/> are always the full day
/// (SPEC F81.3's v1 24/7 scope; no schedule grid exists to narrow them).
/// </summary>
sealed class OptionsMonitorEnvelopeProvider(
    IOptionsMonitor<StationOptions> stationMonitor,
    ILogger<OptionsMonitorEnvelopeProvider> logger) : IEnvelopeProvider
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
