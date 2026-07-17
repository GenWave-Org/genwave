namespace GenWave.Core.Events;

/// <summary>
/// An admin write touched catalog row(s). <paramref name="Change"/> names the mutation
/// ("patch", "eligibility-bulk", "reassign-bulk", "reenrich", "reenrich-bulk", "rating",
/// "rating-bulk", "never-play", "never-play-bulk"). Single-row writes set
/// <paramref name="MediaId"/> with <paramref name="Count"/> = 1;
/// bulk writes carry a null id and the affected-row count — one event per operation, never one per
/// row (a 9000-row bulk curation is one signal).
/// </summary>
public sealed record MediaMutated(string Change, long? MediaId, int Count) : StationEvent;
