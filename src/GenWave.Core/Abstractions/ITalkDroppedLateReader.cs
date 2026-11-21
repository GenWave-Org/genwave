namespace GenWave.Core.Abstractions;

/// <summary>
/// Read port over the <c>talk-dropped-late</c> booth rows (SPEC F219.26, STORY-560): backs the Status
/// page's 7-day count. Admin surface only.
/// </summary>
public interface ITalkDroppedLateReader
{
    /// <summary>
    /// How many <c>talk-dropped-late</c> rows occurred at or after <paramref name="since"/>, or
    /// <see langword="null"/> when the store cannot be reached: unknown, never zero.
    /// </summary>
    Task<int?> CountSinceAsync(DateTimeOffset since, CancellationToken ct);
}
