// STORY-158 — Rank artists and albums from the Catalog (Epic Z / SPEC F61, closes gitea-#233) —
// the WIRE half: POST /api/media/bulk/vote + POST /api/media/bulk/never-play.
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-15, house rule since Epic S).
// Cookie-auth + JSON content-type (F18.7); { filter, direction } / { filter, neverPlay } →
// { updated }; invalid direction/filter 400 ProblemDetails; the per-row RatingController is
// UNTOUCHED — a reviewer seeing scope checks added there fails the change (F33.5, F61.3).
// Entry-point discipline: these scenarios drive the production endpoints, not repo internals.
//
// Idiom mirrors Story112_RatingEndpoints.cs: in-process controller-level tests against a fake
// IMediaRating (no live stack) for business logic, plus a WebApplicationFactory<Program> for the
// content-type posture property of the real HTTP pipeline (mirrors Story112's 415 scenario).

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable, call-recording <see cref="IMediaRating"/> double for the bulk-endpoint scenarios.
/// Separate from Story112_RatingEndpoints.cs's file-scoped fake of the same shape — <c>file</c>
/// visibility keeps the two independent per house convention.
/// </summary>
file sealed class FakeMediaRating : IMediaRating
{
    public VoteOutcome VoteResult { get; set; } = new(RatingWriteResult.Updated, 51);
    public NeverPlayOutcome NeverPlayResult { get; set; } = new(RatingWriteResult.Updated, true);
    public IReadOnlyList<MediaRating> RatingsResult { get; set; } = [];
    public int BulkVoteResult { get; set; }
    public int BulkNeverPlayResult { get; set; }

    public List<(string MediaId, VoteDirection Direction)> VoteCalls { get; } = [];
    public List<(string MediaId, bool NeverPlay)> NeverPlayCalls { get; } = [];
    public List<(MediaQuery Filter, VoteDirection Direction, LibraryScope Scope)> BulkVoteCalls { get; } = [];
    public List<(MediaQuery Filter, bool NeverPlay, LibraryScope Scope)> BulkNeverPlayCalls { get; } = [];

    public Task<VoteOutcome> VoteAsync(string mediaId, VoteDirection direction, CancellationToken ct)
    {
        VoteCalls.Add((mediaId, direction));
        return Task.FromResult(VoteResult);
    }

    public Task<NeverPlayOutcome> SetNeverPlayAsync(string mediaId, bool neverPlay, CancellationToken ct)
    {
        NeverPlayCalls.Add((mediaId, neverPlay));
        return Task.FromResult(NeverPlayResult);
    }

    public Task<IReadOnlyList<MediaRating>> GetRatingsAsync(IReadOnlyList<string> mediaIds, CancellationToken ct)
        => Task.FromResult(RatingsResult);

    public Task<int> BulkVoteAsync(MediaQuery filter, VoteDirection direction, LibraryScope scope, CancellationToken ct)
    {
        BulkVoteCalls.Add((filter, direction, scope));
        return Task.FromResult(BulkVoteResult);
    }

    public Task<int> BulkSetNeverPlayAsync(MediaQuery filter, bool neverPlay, LibraryScope scope, CancellationToken ct)
    {
        BulkNeverPlayCalls.Add((filter, neverPlay, scope));
        return Task.FromResult(BulkNeverPlayResult);
    }
}

/// <summary>Builds a <see cref="BulkRatingController"/> wired to the given fake and station scope.</summary>
file static class BulkRatingControllerFactory
{
    public static BulkRatingController Build(FakeMediaRating rating, LibraryScope stationScope) =>
        new(rating, new FakeStationScopeProvider(stationScope), new FakeSafeScopeProvider(),
            NullLogger<BulkRatingController>.Instance);
}

// ── WebApplicationFactory for the content-type posture AC ────────────────────────────────────────

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline
/// (routing, content-type negotiation) while removing hosted services that would attempt real
/// Liquidsoap/Postgres connections. Mirrors Story112's <c>RatingApiWebFactory</c>.
/// </summary>
file sealed class BulkRatingApiWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint
        // so ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        // AddMediaLibrary reads the Library connection string at composition time in Program.cs —
        // UseSetting (colon-form) reaches that read (verified empirically), so no process env var
        // is mutated and no other test class can race with this per-instance value. A
        // non-reachable host is fine: this scenario never resolves IMediaRating —
        // [Consumes("application/json")] rejects the request during routing/action-selection,
        // before BulkRatingController is constructed.
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();
        });
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureBulkRatingEndpoints
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — the two endpoints
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioBulkVoteEndpoint
    {
        [Fact]
        public async Task AValidUpVoteSweepReturnsTheUpdatedCount()
        {
            // POST /api/media/bulk/vote { filter: { artistExact: "Queen" }, direction: "up" }
            // → 200 { updated: N } (F61.1).
            var fake = new FakeMediaRating { BulkVoteResult = 4 };
            var controller = BulkRatingControllerFactory.Build(fake, new LibraryScope([1]));
            var request = new BulkVoteRequest(
                new BulkRatingFilter(State: null, Artist: null, Genre: null, LibraryId: null, Q: null, ArtistExact: "Queen"),
                "up");

            var result = await controller.BulkVote(request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(4, GetProperty<int>(ok.Value, "updated"));
        }

        [Fact]
        public async Task ADownVoteSweepDecrementsMatchedScores()
        {
            // direction: "down" reaches the repository as VoteDirection.Down — the repo (Z5,
            // MediaLibrary.Tests) proves the actual clamp-and-decrement SQL; this controller-level
            // spec proves the direction is parsed and passed through unchanged.
            var fake = new FakeMediaRating { BulkVoteResult = 2 };
            var controller = BulkRatingControllerFactory.Build(fake, new LibraryScope([1]));
            var request = new BulkVoteRequest(
                new BulkRatingFilter(State: null, Artist: null, Genre: null, LibraryId: null, Q: null, ArtistExact: "Queen"),
                "Down");

            var result = await controller.BulkVote(request, CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            var call = Assert.Single(fake.BulkVoteCalls);
            Assert.Equal(VoteDirection.Down, call.Direction);
        }

        [Fact]
        public async Task TheExactFilterFieldsReachTheSharedWhereBuilder()
        {
            // artistExact/albumExact/genresExact (F52.4) flow through to the MediaQuery the
            // repository's shared BuildAdminWhere consumes (F61.1's "one shared WHERE builder") —
            // proves the DTO→MediaQuery mapping, not the SQL itself (repo-level, Z5).
            var fake = new FakeMediaRating();
            var controller = BulkRatingControllerFactory.Build(fake, new LibraryScope([1]));
            var filter = new BulkRatingFilter(
                State: null, Artist: null, Genre: null, LibraryId: null, Q: null,
                ArtistExact: "Queen", AlbumExact: "A Night at the Opera", GenresExact: ["Rock", "Prog"]);
            var request = new BulkVoteRequest(filter, "up");

            await controller.BulkVote(request, CancellationToken.None);

            var call = Assert.Single(fake.BulkVoteCalls);
            Assert.Equal("Queen", call.Filter.ArtistExact);
            Assert.Equal("A Night at the Opera", call.Filter.AlbumExact);
            Assert.Equal(["Rock", "Prog"], call.Filter.GenresExact);
        }
    }

    public sealed class ScenarioBulkNeverPlayEndpoint
    {
        [Fact]
        public async Task ASetSweepReturnsTheUpdatedCount()
        {
            // POST /api/media/bulk/never-play { filter: { artistExact: "Queen" }, neverPlay: true }
            // → 200 { updated: N } (F61.1, F61.2).
            var fake = new FakeMediaRating { BulkNeverPlayResult = 6 };
            var controller = BulkRatingControllerFactory.Build(fake, new LibraryScope([1]));
            var request = new BulkNeverPlayRequest(
                new BulkRatingFilter(State: null, Artist: null, Genre: null, LibraryId: null, Q: null, ArtistExact: "Queen"),
                true);

            var result = await controller.BulkSetNeverPlay(request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(6, GetProperty<int>(ok.Value, "updated"));
            var call = Assert.Single(fake.BulkNeverPlayCalls);
            Assert.True(call.NeverPlay);
        }

        [Fact]
        public async Task ARestoreSweepClearsTheFlag()
        {
            // A later sweep with neverPlay: false restores the matched rows — never a one-way door
            // (F61.2).
            var fake = new FakeMediaRating { BulkNeverPlayResult = 6 };
            var controller = BulkRatingControllerFactory.Build(fake, new LibraryScope([1]));
            var request = new BulkNeverPlayRequest(
                new BulkRatingFilter(State: null, Artist: null, Genre: null, LibraryId: null, Q: null, ArtistExact: "Queen"),
                false);

            var result = await controller.BulkSetNeverPlay(request, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(6, GetProperty<int>(ok.Value, "updated"));
            var call = Assert.Single(fake.BulkNeverPlayCalls);
            Assert.False(call.NeverPlay);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioInvalidInputRejects
    {
        [Fact]
        public async Task AnInvalidDirectionReturns400AndWritesNothing()
        {
            // {"direction":"sideways"} → 400 ProblemDetails; the fake records no write (F33.3, F61.1).
            var fake = new FakeMediaRating();
            var controller = BulkRatingControllerFactory.Build(fake, new LibraryScope([1]));
            var request = new BulkVoteRequest(
                new BulkRatingFilter(State: null, Artist: null, Genre: null, LibraryId: null, Q: null),
                "sideways");

            var result = await controller.BulkVote(request, CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
            Assert.Empty(fake.BulkVoteCalls);
        }

        [Fact]
        public async Task AMalformedFilterReturns400()
        {
            // A missing/null filter is the malformed-body case both bulk endpoints reject before
            // touching the repository (F61.1) — asserted against both routes since each carries its
            // own null check.
            var fakeVote = new FakeMediaRating();
            var voteController = BulkRatingControllerFactory.Build(fakeVote, new LibraryScope([1]));
            var voteResult = await voteController.BulkVote(
                new BulkVoteRequest(Filter: null, Direction: "up"), CancellationToken.None);

            var voteBadRequest = Assert.IsType<BadRequestObjectResult>(voteResult);
            Assert.IsType<ProblemDetails>(voteBadRequest.Value);
            Assert.Empty(fakeVote.BulkVoteCalls);

            var fakeNeverPlay = new FakeMediaRating();
            var neverPlayController = BulkRatingControllerFactory.Build(fakeNeverPlay, new LibraryScope([1]));
            var neverPlayResult = await neverPlayController.BulkSetNeverPlay(
                new BulkNeverPlayRequest(Filter: null, NeverPlay: true), CancellationToken.None);

            var neverPlayBadRequest = Assert.IsType<BadRequestObjectResult>(neverPlayResult);
            Assert.IsType<ProblemDetails>(neverPlayBadRequest.Value);
            Assert.Empty(fakeNeverPlay.BulkNeverPlayCalls);
        }

        [Fact]
        public async Task AMissingJsonContentTypeIsRejected()
        {
            // Form content-type → 415; nothing written (F18.7 CSRF posture, mirrors Story112).
            await using var factory = new BulkRatingApiWebFactory();
            var client = factory.CreateClient();

            var body = new StringContent(
                "direction=up",
                System.Text.Encoding.UTF8,
                "application/x-www-form-urlencoded");
            var response = await client.PostAsync("/api/media/bulk/vote", body);

            // [Consumes("application/json")] returns 415 Unsupported Media Type.
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }
    }

    public sealed class ScenarioPerRowEndpointsKeepTheirExemption
    {
        [Fact]
        public async Task APerRowVoteOnAnOutOfScopeRowStillSucceeds()
        {
            // POST /api/media/{id}/vote on a row outside the station scope — RatingController
            // takes no IStationScopeProvider dependency at all (F33.5) and this file adds none;
            // the request reaches the fake regardless of scope (F61.3: the bulk-only contrast,
            // Z6's own controller is separate — see BulkRatingController).
            var fake = new PerRowFakeMediaRating { VoteResult = new VoteOutcome(RatingWriteResult.Updated, 51) };
            var controller = new RatingController(fake);

            var result = await controller.Vote(500, new VoteRequest("up"), CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Single(fake.VoteCalls);
        }

        [Fact]
        public async Task APerRowNeverPlayOnAnOutOfScopeRowStillSucceeds()
        {
            // Same arrange; PUT never-play → 200 (F33.5, F61.3 — the exemption is neither extended
            // to bulk nor narrowed by this task).
            var fake = new PerRowFakeMediaRating { NeverPlayResult = new NeverPlayOutcome(RatingWriteResult.Updated, true) };
            var controller = new RatingController(fake);

            var result = await controller.SetNeverPlay(500, new NeverPlayRequest(true), CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Single(fake.NeverPlayCalls);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Reads a property off an anonymous-object action result value via reflection.</summary>
    static T GetProperty<T>(object? value, string name)
    {
        var prop = value?.GetType().GetProperty(name)
            ?? throw new InvalidOperationException($"Property '{name}' not found on {value?.GetType()}.");
        return (T)(prop.GetValue(value) ?? throw new InvalidOperationException($"Property '{name}' was null."));
    }
}

/// <summary>
/// Minimal <see cref="IMediaRating"/> double for the per-row exemption scenarios — only the two
/// methods <see cref="RatingController"/> calls in this file's tests are scripted; the bulk methods
/// are never exercised through this fake (that is <c>FakeMediaRating</c>'s job, above).
/// </summary>
file sealed class PerRowFakeMediaRating : IMediaRating
{
    public VoteOutcome VoteResult { get; set; } = new(RatingWriteResult.Updated, 51);
    public NeverPlayOutcome NeverPlayResult { get; set; } = new(RatingWriteResult.Updated, true);

    public List<(string MediaId, VoteDirection Direction)> VoteCalls { get; } = [];
    public List<(string MediaId, bool NeverPlay)> NeverPlayCalls { get; } = [];

    public Task<VoteOutcome> VoteAsync(string mediaId, VoteDirection direction, CancellationToken ct)
    {
        VoteCalls.Add((mediaId, direction));
        return Task.FromResult(VoteResult);
    }

    public Task<NeverPlayOutcome> SetNeverPlayAsync(string mediaId, bool neverPlay, CancellationToken ct)
    {
        NeverPlayCalls.Add((mediaId, neverPlay));
        return Task.FromResult(NeverPlayResult);
    }

    public Task<IReadOnlyList<MediaRating>> GetRatingsAsync(IReadOnlyList<string> mediaIds, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<MediaRating>>([]);

    public Task<int> BulkVoteAsync(MediaQuery filter, VoteDirection direction, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by the per-row exemption scenarios.");

    public Task<int> BulkSetNeverPlayAsync(MediaQuery filter, bool neverPlay, LibraryScope scope, CancellationToken ct)
        => throw new NotSupportedException("Not exercised by the per-row exemption scenarios.");
}
