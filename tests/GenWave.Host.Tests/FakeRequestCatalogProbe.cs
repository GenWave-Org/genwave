using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IRequestCatalogProbe"/> double (STORY-226, PLAN T89): hands back whatever
/// <see cref="Result"/> is currently set to (default <see langword="null"/> — "no catalog match"),
/// and records every call's artist/title so a spec can assert exactly what predicate reached the
/// probe without a real Postgres connection.
/// </summary>
sealed class FakeRequestCatalogProbe : IRequestCatalogProbe
{
    public long? Result { get; set; }
    public List<(string? Artist, string? Title)> Calls { get; } = [];

    public Task<long?> FindBestAsync(string? artist, string? title, CancellationToken ct)
    {
        Calls.Add((artist, title));
        return Task.FromResult(Result);
    }

    // Not exercised by STORY-226 specs (this fake's own scope) — STORY-227's fulfillment-rung facts
    // drive GenWave.Orchestration.Tests' own FakeRequestCatalogProbe instead.
    public Task<MediaReference?> GetSelectableByIdAsync(long mediaId, SegmentEnvelope? envelope, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-226 specs.");

    public Task<MediaReference?> FindVibeAsync(IReadOnlyList<string> moods, SegmentEnvelope? envelope, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-226 specs.");
}
