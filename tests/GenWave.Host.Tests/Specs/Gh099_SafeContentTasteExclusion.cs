// gh-#99 — Safe loop / station IDs must never allow ranking or taste voting
//
// BDD specification — xUnit. Safe-scope content (the seeded safe loop, authored safe segments,
// station IDs — rows whose library_id falls in Station:SafeScope:LibraryIds) is functional audio,
// not rateable music:
//   • POST /api/media/{id}/vote and PUT /api/media/{id}/never-play refuse it (403) — the repository
//     answers RatingWriteResult.SafeContentExcluded and the controller maps it here.
//   • The bulk sweeps refuse a filter that explicitly NAMES a safe library (403, loud) — an unnamed
//     filter is carved out silently inside the repository's WHERE instead.
//   • POST /api/booth-log/{id}/taste-thumb refuses an airing stamped with a safe-scope media id
//     (400) BEFORE the accrual store is ever reached — a thumb on a safe play would teach the
//     persona an artist rule for the STATION's own name.
//   • GET /api/booth-log flags such rows tasteExcluded so the UI renders no thumb control at all.
//
// Repository-level facts (the SQL carve-outs, rateable stamping, ListAdmin projection) live in
// GenWave.MediaLibrary.Tests/Specs/Gh099_SafeContentRatingRepository.cs against real Postgres —
// this file proves the CONTROLLER contracts with the same direct-construction idiom as
// Story112_RatingEndpoints.cs / Story215_TasteLearningGuardrails.cs.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

/// <summary>Scriptable <see cref="IMediaRating"/> double — outcome-settable, call-recording.</summary>
file sealed class ExclusionFakeMediaRating : IMediaRating
{
    public VoteOutcome VoteResult { get; set; } = new(RatingWriteResult.Updated, 51);
    public NeverPlayOutcome NeverPlayResult { get; set; } = new(RatingWriteResult.Updated, true);

    public List<(MediaQuery Filter, VoteDirection Direction, LibraryScope Scope)> BulkVoteCalls { get; } = [];
    public List<(MediaQuery Filter, bool NeverPlay, LibraryScope Scope)> BulkNeverPlayCalls { get; } = [];

    public Task<VoteOutcome> VoteAsync(string mediaId, VoteDirection direction, CancellationToken ct) =>
        Task.FromResult(VoteResult);

    public Task<NeverPlayOutcome> SetNeverPlayAsync(string mediaId, bool neverPlay, CancellationToken ct) =>
        Task.FromResult(NeverPlayResult);

    public Task<IReadOnlyList<MediaRating>> GetRatingsAsync(IReadOnlyList<string> mediaIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MediaRating>>([]);

    public Task<int> BulkVoteAsync(MediaQuery filter, VoteDirection direction, LibraryScope scope, CancellationToken ct)
    {
        BulkVoteCalls.Add((filter, direction, scope));
        return Task.FromResult(0);
    }

    public Task<int> BulkSetNeverPlayAsync(MediaQuery filter, bool neverPlay, LibraryScope scope, CancellationToken ct)
    {
        BulkNeverPlayCalls.Add((filter, neverPlay, scope));
        return Task.FromResult(0);
    }
}

/// <summary>Fixed-page, media-id-aware <see cref="IBoothLogReader"/> double.</summary>
file sealed class ExclusionFakeBoothLogReader(IReadOnlyList<BoothLogEntry> rows) : IBoothLogReader
{
    public Task<BoothLogPage> ReadAsync(BoothLogCursor? before, int take, CancellationToken ct) =>
        Task.FromResult(new BoothLogPage(rows.Take(take).ToList(), NextBefore: null));

    public Task<long?> GetMediaIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(rows.FirstOrDefault(e => e.Id == id)?.MediaId);
}

/// <summary>Recording <see cref="IPersonaTasteAccrualStore"/> — every reached call is a fact.</summary>
file sealed class RecordingAccrualStore : IPersonaTasteAccrualStore
{
    public List<(long BoothLogId, TasteThumbDirection Direction)> ThumbCalls { get; } = [];

    public Task<TasteThumbOutcome> ThumbAsync(long boothLogId, TasteThumbDirection direction, CancellationToken ct)
    {
        ThumbCalls.Add((boothLogId, direction));
        return Task.FromResult<TasteThumbOutcome>(new TasteThumbOutcome.Nudged(PersonaId: 7, Weight: 0.2));
    }
}

/// <summary>Shared fixture wiring for the booth-log facts: library 9 is the safe library.</summary>
file static class ExclusionFixture
{
    public const long SafeLibraryId = 9;
    public const long MusicLibraryId = 1;

    public const long SafeRowId = 5;
    public const long MusicRowId = 6;
    public const long SafeMediaId = 42;
    public const long MusicMediaId = 43;

    public static readonly IReadOnlyList<BoothLogEntry> Rows =
    [
        new(SafeRowId, DateTime.UtcNow, "track-started", "Started 'Please Stand By' by GenWave", PersonaId: 7, Pick: null, MediaId: SafeMediaId),
        new(MusicRowId, DateTime.UtcNow.AddMinutes(-3), "track-started", "Started 'Song' by Someone", PersonaId: 7, Pick: null, MediaId: MusicMediaId),
    ];

    public static BoothLogController Controller(RecordingAccrualStore accrual) => new(
        new ExclusionFakeBoothLogReader(Rows),
        accrual,
        new FakeMediaLibraryMembership(new Dictionary<long, long>
        {
            [SafeMediaId] = SafeLibraryId,
            [MusicMediaId] = MusicLibraryId,
        }),
        new FakeSafeScopeProvider(SafeLibraryId),
        NullLogger<BoothLogController>.Instance);
}

public static class FeatureSafeContentTasteExclusion
{
    // ---------------------------------------------------------------------
    // Per-row rating endpoints (F33 surface)
    // ---------------------------------------------------------------------

    public static class ScenarioPerRowRatingRefusesSafeContent
    {
        [Fact]
        public static async Task AVoteOnSafeContentReturns403AndWritesNothing()
        {
            var rating = new ExclusionFakeMediaRating { VoteResult = new(RatingWriteResult.SafeContentExcluded, null) };
            var controller = new RatingController(rating);

            var result = await controller.Vote(42, new VoteRequest("up"), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        }

        [Fact]
        public static async Task ANeverPlaySetOnSafeContentReturns403()
        {
            var rating = new ExclusionFakeMediaRating { NeverPlayResult = new(RatingWriteResult.SafeContentExcluded, null) };
            var controller = new RatingController(rating);

            var result = await controller.SetNeverPlay(42, new NeverPlayRequest(true), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // Bulk sweeps (F61 surface)
    // ---------------------------------------------------------------------

    public static class ScenarioBulkSweepRefusesANamedSafeLibrary
    {
        static BulkRatingController Controller(IMediaRating rating) => new(
            rating,
            new FakeStationScopeProvider(new LibraryScope([ExclusionFixture.MusicLibraryId])),
            new FakeSafeScopeProvider(ExclusionFixture.SafeLibraryId),
            NullLogger<BulkRatingController>.Instance);

        static BulkRatingFilter NamingLibrary(long libraryId) =>
            new(State: null, Artist: null, Genre: null, LibraryId: libraryId, Q: null);

        [Fact]
        public static async Task ABulkVoteNamingTheSafeLibraryReturns403WithoutReachingTheRepository()
        {
            var rating = new ExclusionFakeMediaRating();
            var controller = Controller(rating);

            var result = await controller.BulkVote(
                new BulkVoteRequest(NamingLibrary(ExclusionFixture.SafeLibraryId), "up"), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
            Assert.Empty(rating.BulkVoteCalls);
        }

        [Fact]
        public static async Task ABulkNeverPlayNamingTheSafeLibraryReturns403WithoutReachingTheRepository()
        {
            var rating = new ExclusionFakeMediaRating();
            var controller = Controller(rating);

            var result = await controller.BulkSetNeverPlay(
                new BulkNeverPlayRequest(NamingLibrary(ExclusionFixture.SafeLibraryId), true), CancellationToken.None);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
            Assert.Empty(rating.BulkNeverPlayCalls);
        }

        [Fact]
        public static async Task ABulkVoteNamingAMusicLibraryStillReachesTheRepository()
        {
            var rating = new ExclusionFakeMediaRating();
            var controller = Controller(rating);

            var result = await controller.BulkVote(
                new BulkVoteRequest(NamingLibrary(ExclusionFixture.MusicLibraryId), "up"), CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Single(rating.BulkVoteCalls);
        }
    }

    // ---------------------------------------------------------------------
    // Taste thumbs (F84 surface)
    // ---------------------------------------------------------------------

    public static class ScenarioTasteThumbRefusesASafeAiring
    {
        [Fact]
        public static async Task AThumbOnASafeAiringReturns400BeforeTheAccrualStoreIsReached()
        {
            var accrual = new RecordingAccrualStore();
            var controller = ExclusionFixture.Controller(accrual);

            var result = await controller.ThumbTaste(
                ExclusionFixture.SafeRowId, new TasteThumbRequest("up"), CancellationToken.None);

            var problem = Assert.IsType<BadRequestObjectResult>(result);
            var details = Assert.IsType<ProblemDetails>(problem.Value);
            Assert.Contains("gh-#99", details.Detail);
            Assert.Empty(accrual.ThumbCalls);
        }

        [Fact]
        public static async Task AThumbOnAMusicAiringStillReachesTheAccrualStore()
        {
            var accrual = new RecordingAccrualStore();
            var controller = ExclusionFixture.Controller(accrual);

            var result = await controller.ThumbTaste(
                ExclusionFixture.MusicRowId, new TasteThumbRequest("up"), CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Single(accrual.ThumbCalls);
        }
    }

    // ---------------------------------------------------------------------
    // Booth-log feed flag (the UI's no-control gate)
    // ---------------------------------------------------------------------

    public static class ScenarioBoothLogFlagsSafeRows
    {
        [Fact]
        public static async Task TheFeedMarksSafeAiringsTasteExcludedAndMusicAiringsNot()
        {
            var controller = ExclusionFixture.Controller(new RecordingAccrualStore());

            var result = await controller.List(before: null, take: null, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var page = Assert.IsType<BoothLogPageDto>(ok.Value);
            Assert.True(page.Entries.Single(e => e.Id == ExclusionFixture.SafeRowId).TasteExcluded);
            Assert.False(page.Entries.Single(e => e.Id == ExclusionFixture.MusicRowId).TasteExcluded);
        }
    }
}
