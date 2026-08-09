namespace GenWave.Host.Options;

/// <summary>
/// The station's broadcast location within the Station config section (SPEC F108.1/F108.3, PLAN
/// T226) — read live through <see cref="OptionsMonitorStationLocationProvider"/> by
/// <c>GenWave.Context.Weather.WeatherContextProvider</c>. Every field is a raw string, deliberately
/// unvalidated here: <see cref="GenWave.Core.Domain.StationLocation"/>'s own remarks make "blank or
/// invalid coordinates" each coordinate-consuming provider's own fail-closed check, not something
/// either this class or <see cref="Configuration.SettingValidator"/> decides on a provider's behalf.
/// Bound to <c>Station:Location</c>.
/// </summary>
public sealed class StationLocationOptions
{
    /// <summary>Precise latitude — never spoken or logged (SPEC F108.3's spoken-vs-precise split).
    /// Blank (the default) means no coordinate is configured.</summary>
    public string Latitude { get; set; } = string.Empty;

    /// <summary>Precise longitude — same posture as <see cref="Latitude"/>.</summary>
    public string Longitude { get; set; } = string.Empty;

    /// <summary>The only location string a provider may put in a fact, prompt, or log line (SPEC
    /// F108.3). Blank (the default) means no place name is ever spoken.</summary>
    public string SpokenName { get; set; } = string.Empty;
}
