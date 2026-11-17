namespace GenWave.Core.Domain;

/// <summary>
/// The station-wide callback numbers for the last 7 days (SPEC F218.47), summed across every DJ's row of
/// <c>station.callback_rate</c>: offers made, offers that aired, and DJ-hours on air.
/// </summary>
public sealed record CallbackRate(int Asks, int Used, int Hours)
{
    /// <summary>
    /// Used per DJ-hour: total used over total hours (not the mean of per-DJ rates). <see langword="null"/>
    /// with no offers ("None yet") and with no DJ-hours to divide by: with zero hours the rate is undefined,
    /// and the page has nothing honest to show either way.
    /// </summary>
    public decimal? PerDjHour => Asks == 0 || Hours == 0 ? null : (decimal)Used / Hours;
}
