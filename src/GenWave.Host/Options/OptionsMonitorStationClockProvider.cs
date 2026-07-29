using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IStationClockProvider"/> seam (gh-#117): wraps
/// <see cref="IOptionsMonitor{TOptions}"/> so every "station-local now" read — the prompt's clock
/// line, <c>SegmentRequest.LocalNow</c> stamping, the persona preview, and (gh-#224) the schedule
/// grid's slot resolution and the taste day/hour gates —
/// resolves the SAME live <c>Station:Timezone</c> value, through the SAME idiom
/// <see cref="OptionsMonitorAudiencePostureProvider"/> established for the audience posture.
///
/// Re-resolves <see cref="StationOptions.Timezone"/> through
/// <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> on every call — nothing cached here;
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> already is the cache, so a live
/// <c>Station:Timezone</c> edit governs the very next prompt build. Empty falls back to
/// <see cref="TimeProvider.LocalTimeZone"/> (the container's own clock — pre-gh-#117 behavior,
/// byte-identical); so does an unresolvable id, which can only arrive via the environment
/// (<c>SettingValidator</c> 400s one on the settings-API path) — a config typo must never fault
/// the patter path, it just keeps the container's clock.
/// </summary>
sealed class OptionsMonitorStationClockProvider(
    IOptionsMonitor<StationOptions> stationMonitor,
    TimeProvider timeProvider) : IStationClockProvider
{
    public DateTimeOffset LocalNow =>
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ResolveTimeZone());

    public TimeZoneInfo Zone => ResolveTimeZone();

    TimeZoneInfo ResolveTimeZone()
    {
        var id = stationMonitor.CurrentValue.Timezone;
        if (string.IsNullOrWhiteSpace(id))
            return timeProvider.LocalTimeZone;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return timeProvider.LocalTimeZone;
        }
        catch (InvalidTimeZoneException)
        {
            return timeProvider.LocalTimeZone;
        }
    }
}
