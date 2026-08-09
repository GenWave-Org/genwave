namespace GenWave.Context.Weather;

using System.Text.Json.Serialization;

/// <summary>
/// The <c>daily</c> section of an <see cref="OpenMeteoResponse"/> — today's near-forecast
/// (SPEC F108.2's "short conditions/near-forecast blurb"). Requested with <c>forecast_days=1</c>, so
/// index <c>0</c> is always today; each list is null (never fetched) or empty (an unexpected/short
/// reply) exactly when there is nothing to report, never a fault this provider needs to distinguish.
/// </summary>
sealed record OpenMeteoDaily(
    [property: JsonPropertyName("temperature_2m_max")] IReadOnlyList<double>? Temperature2mMax,
    [property: JsonPropertyName("temperature_2m_min")] IReadOnlyList<double>? Temperature2mMin);
