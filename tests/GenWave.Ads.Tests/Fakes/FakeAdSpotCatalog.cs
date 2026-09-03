using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Ads.Tests.Fakes;

/// <summary>
/// <see cref="IMediaCatalog"/> double exercising only <see cref="GetRandomReadyAdSpotAsync"/> —
/// <see cref="LibraryAdSpotSource"/>'s one call — via a fixed, in-memory pool of ready ad rows.
/// Deterministic (first non-excluded, in insertion order, never random) so a scenario proving the
/// anti-repeat ring's own exclusion set can assert exactly which id comes back next, rather than
/// looping to exhaustion the way the live-Postgres <c>random()</c>-backed fact does. Every other
/// <see cref="IMediaCatalog"/> member is unreachable from <see cref="LibraryAdSpotSource"/> and
/// throws if ever called, so an accidental new call site fails loudly instead of silently returning
/// nothing.
/// </summary>
public sealed class FakeAdSpotCatalog : IMediaCatalog
{
    readonly List<MediaReference> pool = [];

    public IReadOnlyList<string> LastExcludeIds { get; private set; } = [];
    public LibraryScope? LastScope { get; private set; }
    public int CallCount { get; private set; }

    public FakeAdSpotCatalog AddReady(string mediaId, string locator = "/authored/ads/spot.wav")
    {
        // Fully qualified (PLAN T400 review F2 — see Story388_AdSpotPipeline.Spot's own remarks: the
        // GenWave.Tts ProjectReference this project gained pulls in GenWave.Loudness transitively,
        // whose root namespace now shadows the unqualified "Loudness" identifier).
        pool.Add(new MediaReference(
            mediaId, locator, $"Spot {mediaId}", new GenWave.Core.Domain.Loudness(-14.0, -1.0, true),
            DurationMs: 30_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Artist: "Station Name", Album: null, Genre: null, Year: null));
        return this;
    }

    public Task<MediaReference?> GetRandomReadyAdSpotAsync(
        LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct)
    {
        CallCount++;
        LastExcludeIds = excludeIds;
        LastScope = scope;

        if (scope.IsEmpty)
            return Task.FromResult<MediaReference?>(null);

        var pick = pool.FirstOrDefault(m => !excludeIds.Contains(m.MediaId));
        return Task.FromResult<MediaReference?>(pick);
    }

    public Task<MediaReference?> GetByIdAsync(LibraryScope scope, string mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<MediaReference?> GetByIdUnscopedAsync(string mediaId, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<MediaReference?> GetRandomReadyAsync(LibraryScope scope, IReadOnlyList<string> excludeIds, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<RotationCandidate?> GetRotationCandidateAsync(
        LibraryScope scope, IReadOnlyList<string> orderedRecentIds, int artistSeparation, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<PagedResult<MediaReference>> ListAsync(LibraryScope scope, MediaQuery query, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<CatalogStatusCounts> GetStatusCountsAsync(LibraryScope safeScope, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");

    public Task<IReadOnlyList<FacetValue>> GetFacetsAsync(FacetField field, LibraryScope scope, CancellationToken ct) =>
        throw new NotSupportedException("Not used by LibraryAdSpotSource.");
}
