using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Read port over <c>station.callback_rate</c> (SPEC F218.47, STORY-551): backs the Status page's
/// "Callbacks per DJ-hour" number. Admin surface only.
/// </summary>
public interface ICallbackRateReader
{
    /// <summary>
    /// The station-wide last-7-days numbers, or <see langword="null"/> when the store cannot be reached: a
    /// side number must never take the status endpoint down with it.
    /// </summary>
    Task<CallbackRate?> ReadAsync(CancellationToken ct);
}
