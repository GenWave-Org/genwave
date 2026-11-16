using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Read port over the repeat views (SPEC F218.29, STORY-542): backs the Status page's Repeats panel. Admin
/// surface only; never exposed to spectators (F218.26).
/// </summary>
public interface IRepeatNumbersReader
{
    /// <summary>
    /// The last-7-days numbers, or <see langword="null"/> when the store cannot be reached: a side panel must
    /// never take the status endpoint down with it.
    /// </summary>
    Task<RepeatNumbers?> ReadAsync(CancellationToken ct);
}
