using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Host.Tests;

/// <summary>
/// Scriptable <see cref="IRequestCatalogProbe"/> double (STORY-226, PLAN T89; gh-#131 genre members):
/// hands back whatever <see cref="Result"/> is currently set to (default <see langword="null"/> —
/// "no catalog match"), and records every call's artist/title/genre so a spec can assert exactly
/// what predicate reached the probe without a real Postgres connection.
/// <see cref="RequestableGenres"/> scripts the gh-#131 genre surface: it answers both
/// <see cref="ListRequestableGenresAsync"/> (verbatim) and <see cref="HasRequestableGenreAsync"/>
/// (case-insensitive membership — the real repository's <c>lower() = lower()</c> semantics).
/// </summary>
sealed class FakeRequestCatalogProbe : IRequestCatalogProbe
{
    public long? Result { get; set; }
    public List<(string? Artist, string? Title, string? Genre)> Calls { get; } = [];

    /// <summary>The live requestable-genre list this fake publishes — default empty ("station has
    /// no genres"), the conservative pre-#131 posture no existing spec depended on.</summary>
    public List<string> RequestableGenres { get; set; } = [];

    public Task<long?> FindBestAsync(string? artist, string? title, string? genre, CancellationToken ct)
    {
        Calls.Add((artist, title, genre));
        return Task.FromResult(Result);
    }

    public Task<bool> HasRequestableGenreAsync(string genre, CancellationToken ct) =>
        Task.FromResult(RequestableGenres.Any(
            option => string.Equals(option, genre, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<string>> ListRequestableGenresAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(RequestableGenres.ToList());

    // Not exercised by STORY-226 specs (this fake's own scope) — STORY-227's fulfillment-rung facts
    // drive GenWave.Orchestration.Tests' own FakeRequestCatalogProbe instead.
    public Task<MediaReference?> GetSelectableByIdAsync(long mediaId, SegmentEnvelope? envelope, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-226 specs.");

    public Task<MediaReference?> FindVibeAsync(
        IReadOnlyList<string> moods, string? genre, SegmentEnvelope? envelope, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by this fake's own STORY-226 specs.");
}
