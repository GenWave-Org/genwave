namespace GenWave.Context.Weather;

using System.Text.Json.Serialization;

/// <summary>
/// Wire shape of an Open-Meteo <c>GET /v1/forecast</c> response (SPEC F108.1) — only the top-level
/// sections <see cref="WeatherContextProvider"/> asks for (<c>current</c> for conditions,
/// <c>daily</c> for today's high/low). No <c>current_units</c> property (F1 fix, T227 review): that
/// section is unvalidated third-party text a crafted reply could use to forge the single-line-fact
/// invariant, and this provider's own request already pins the units it needs
/// (<see cref="WeatherContextProvider.BuildRequestUri"/>'s <c>temperature_unit</c>/
/// <c>wind_speed_unit</c> query params) — so there is nothing this property would ever be used for.
/// Field selection decided at T227 build time against a real payload (curl'd from
/// api.open-meteo.com — keyless): see <see cref="WeatherContextProvider"/>'s own remarks for the
/// exact shape and the request query it was verified against.
/// </summary>
sealed record OpenMeteoResponse(
    [property: JsonPropertyName("current")] OpenMeteoCurrent? Current,
    [property: JsonPropertyName("daily")] OpenMeteoDaily? Daily);
