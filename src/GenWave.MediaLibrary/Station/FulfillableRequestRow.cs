using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Station;

/// <summary>
/// Dapper projection for <see cref="RequestRepository.GetOldestLiveAsync"/> (SPEC F87.6, STORY-227,
/// PLAN T90). Settable properties, not a positional record — <c>Moods</c>' <c>text[]</c> column
/// reports as the general <see cref="Array"/> CLR type through the reader, which Dapper's stricter
/// constructor-matching (the path a positional record takes) rejects; the property-setter path this
/// shape falls back to coerces it instead — the exact same story <c>Story224_RequestStore.RequestRow</c>'s
/// own remarks document for this project's test-side twin of this row.
/// </summary>
sealed record FulfillableRequestRow
{
    public long Id { get; init; }
    public long? MatchedMediaId { get; init; }
    public string[]? Moods { get; init; }

    public FulfillableRequest ToFulfillableRequest() => new(Id, MatchedMediaId, Moods ?? []);
}
