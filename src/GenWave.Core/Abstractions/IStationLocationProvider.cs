namespace GenWave.Core.Abstractions;

using GenWave.Core.Domain;

/// <summary>
/// The narrow seam a coordinate-consuming <c>GenWave.Context</c> provider (SPEC F108.1, e.g.
/// <c>WeatherContextProvider</c>) reads the station's broadcast location through. Mirrors
/// <see cref="IContextSettingsProvider"/> one concern over: that seam answers "is this provider
/// enabled and how often", this one answers "where is the station" — kept separate because a
/// provider that needs a location is a minority (weather today; most future providers, including
/// the history provider, need neither).
///
/// <para>
/// Implementations MUST re-evaluate <see cref="Current"/> fresh on every call — never cache the
/// result in a field (the same discipline <see cref="IContextSettingsProvider.For"/> follows) — so a
/// live operator edit to <c>Station:Location:*</c> reaches the very next fetch with no process
/// restart. The Host's <c>IOptionsMonitor</c>-backed implementation lands at PLAN T226; until then,
/// <see cref="NoOpStationLocationProvider"/> keeps every coordinate-consuming provider — and every
/// test built against it — compiling and inert.
/// </para>
/// </summary>
public interface IStationLocationProvider
{
    /// <summary>The station's currently configured location, evaluated fresh on every call.</summary>
    StationLocation Current { get; }
}
