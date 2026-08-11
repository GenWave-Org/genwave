using GenWave.Core.Abstractions;

namespace GenWave.Host.Api;

/// <summary>
/// The one shared "what calendar date is it at the station right now" conversion (PLAN T259 review
/// finding 6) — <see cref="SpecialsController.List"/>/<see cref="SpecialsController.Create"/> and
/// <see cref="ShowsController"/>'s own specials-referencing guard all needed the identical
/// <c>DateOnly.FromDateTime(stationClock.LocalNow.DateTime)</c> conversion; extracted here rather than
/// left as three hand-copies so "station-local calendar date, never the container's own" stays
/// expressed exactly once, in one place a future caller finds by IntelliSense on
/// <see cref="IStationClockProvider"/> itself.
/// </summary>
internal static class StationClockProviderExtensions
{
    /// <summary>The station's own current LOCAL calendar date — <see cref="IStationClockProvider.LocalNow"/>'s
    /// date component, never re-resolved through <see cref="DateTime.Today"/> or any other
    /// container-clock source.</summary>
    internal static DateOnly Today(this IStationClockProvider clock) => DateOnly.FromDateTime(clock.LocalNow.DateTime);
}
