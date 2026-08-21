using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IStationImagingSettingsProvider"/> seam (SPEC
/// F110.1/F110.3, PLAN T230): wraps <see cref="IOptionsMonitor{TOptions}"/> so
/// <c>GenWave.Orchestration.ClockAnchoredImagingProducer</c> reads the SAME live value
/// <c>PUT /api/settings</c> writes to <c>Station:Imaging:*</c> (mirrors
/// <see cref="OptionsMonitorStationLocationProvider"/>).
///
/// Builds a new <see cref="StationImagingSettings"/> from <see cref="StationOptions.Imaging"/> on
/// every call — nothing is cached here, the same discipline every sibling provider in this folder
/// follows.
/// </summary>
sealed class OptionsMonitorStationImagingProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IStationImagingSettingsProvider
{
    public StationImagingSettings Current
    {
        get
        {
            var imaging = stationMonitor.CurrentValue.Imaging;
            return new StationImagingSettings(
                imaging.ClockAnchoredIdents, imaging.TimeAnnouncements, imaging.TimeAnnouncementBudgetSeconds);
        }
    }
}
