using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Options;

/// <summary>
/// The Host-side half of the <see cref="IStationLocationProvider"/> seam (SPEC F108.1, PLAN T226):
/// wraps <see cref="IOptionsMonitor{TOptions}"/> so <c>GenWave.Context.Weather.WeatherContextProvider</c>
/// reads the SAME live value <c>PUT /api/settings</c> writes to <c>Station:Location:*</c> (mirrors
/// <see cref="OptionsMonitorStationIdentityProvider"/>).
///
/// Builds a new <see cref="StationLocation"/> from <see cref="StationOptions.Location"/> on every
/// call — nothing is cached here, the same discipline every sibling provider in this folder follows.
/// </summary>
sealed class OptionsMonitorStationLocationProvider(IOptionsMonitor<StationOptions> stationMonitor)
    : IStationLocationProvider
{
    public StationLocation Current
    {
        get
        {
            var location = stationMonitor.CurrentValue.Location;
            return new StationLocation(location.Latitude, location.Longitude, location.SpokenName);
        }
    }
}
