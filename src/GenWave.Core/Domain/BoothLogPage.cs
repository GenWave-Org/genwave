namespace GenWave.Core.Domain;

/// <summary>
/// One newest-first page of <c>station.booth_log</c> rows (SPEC F72.2, STORY-195).
/// <see cref="NextBefore"/> is <see langword="null"/> when <see cref="Entries"/> is the oldest
/// (last) page.
/// </summary>
public sealed record BoothLogPage(IReadOnlyList<BoothLogEntry> Entries, BoothLogCursor? NextBefore);
