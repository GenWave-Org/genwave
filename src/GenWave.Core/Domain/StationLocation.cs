namespace GenWave.Core.Domain;

/// <summary>
/// The station's configured broadcast location (SPEC F108.1/F108.3), read through
/// <see cref="Abstractions.IStationLocationProvider"/>. Every field is a raw string, not a parsed
/// number — this seam makes no promise about whether the underlying config store held a valid
/// coordinate, only about what was configured; validating "blank or invalid" is deliberately each
/// coordinate-consuming provider's own job (e.g. <c>WeatherContextProvider</c>'s fail-closed check),
/// not something this record or its provider decides on a caller's behalf.
/// </summary>
/// <param name="Latitude">
/// <c>Station:Location:Latitude</c> — precise, never spoken or logged (the F108.3 spoken-vs-precise
/// split). Blank or non-numeric is a legal value here; a consumer treats it as "no coordinate".
/// </param>
/// <param name="Longitude">
/// <c>Station:Location:Longitude</c> — same posture as <see cref="Latitude"/>.
/// </param>
/// <param name="SpokenName">
/// <c>Station:Location:SpokenName</c> — the ONLY location string a provider may put in a fact,
/// prompt, or log line (SPEC F108.3). Blank means no place name is ever spoken, not a fallback to
/// the coordinates.
/// </param>
public sealed record StationLocation(string Latitude, string Longitude, string SpokenName);
