namespace GenWave.Context.Weather;

using System.Text.Json.Serialization;

/// <summary>
/// The <c>current</c> section of an <see cref="OpenMeteoResponse"/> — instantaneous conditions.
/// <see cref="WeatherCode"/> is a WMO weather-interpretation code (see
/// <see cref="WeatherContextProvider"/>'s condition-text mapping); <see cref="WindSpeed10m"/> is
/// requested in km/h (<c>wind_speed_unit=kmh</c>) so the "is this wind notable" threshold means the
/// same thing regardless of query changes elsewhere.
/// </summary>
sealed record OpenMeteoCurrent(
    [property: JsonPropertyName("temperature_2m")] double? Temperature2m,
    [property: JsonPropertyName("weather_code")] int? WeatherCode,
    [property: JsonPropertyName("wind_speed_10m")] double? WindSpeed10m);
