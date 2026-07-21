// STORY-112 — Vote, never-play, and batch-read endpoints (WIRE)
//
// BDD specification — xUnit. Drives the deployed entry points (RatingController routes)
// through direct controller construction with a fake IMediaRating at the boundary (mirrors the
// Story103/Story049 controller-spec idiom) — no live stack required; the real-Postgres behavior
// behind the seam is Story110's job (GenWave.MediaLibrary.Tests).
//
// The two posture negatives (401 without a cookie, 415 without JSON) drive the real HTTP pipeline
// via WebApplicationFactory<Program> (mirrors Story058's SettingsApiWebFactory) since they are
// properties of the auth/routing middleware, not of the controller's own logic.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable, call-recording <see cref="IMediaRating"/> double. Returns the configured outcome
/// from each method and records every call's arguments so a scenario can assert what
/// <see cref="RatingController"/> passed through (or, for the bad-direction case, that it never
/// called through at all).
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
    public List<IReadOnlyList<string>> RatingsCalls { get; } = [];
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
    {
        RatingsCalls.Add(mediaIds);
        return Task.FromResult(RatingsResult);
    }

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

// ── WebApplicationFactory for auth/content-type AC tests ─────────────────────────────────────────

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline
/// (routing, auth, content-type negotiation) while removing hosted services that would attempt
/// real Liquidsoap/Postgres connections. Mirrors Story058's <c>SettingsApiWebFactory</c>.
///
/// <paramref name="withAdminPassword"/> controls whether the deny-by-default fallback
/// authorization policy is active (the 401 case needs it; the 415 case does not — [Consumes]
/// rejection happens during routing/action-selection, before auth runs, so it is tested with the
/// API left open).
/// </summary>
file sealed class RatingApiWebFactory(bool withAdminPassword) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint
        // so ValidateOnStart() is satisfied without injecting them manually.
        builder.UseEnvironment("Development");

        // AddMediaLibrary reads the Library connection string at composition time in Program.cs —
        // UseSetting (colon-form) reaches that read (verified empirically), so no process env var
        // is mutated and no other test class can race with this per-instance value. A
        // non-reachable host is fine: neither of these two scenarios ever resolves IMediaRating —
        // the request is rejected by auth/routing middleware before RatingController is constructed.
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        if (withAdminPassword)
        {
            builder.UseSetting("Admin:Password", "test-password-x7z");
        }

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL hosted services — no Liquidsoap or DB connections during this test.
            services.RemoveAll<IHostedService>();
        });
    }
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureRatingEndpoints
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the three routes round-trip
    // ---------------------------------------------------------------------

    public sealed class ScenarioVotingOnACatalogRow
    {
        [Fact]
        public async Task VoteUpReturns200WithThePostVoteScore()
        {
            // POST /api/media/42/vote {"direction":"up"} → 200 {"score":51} (F33.3).
            // Also proves case-insensitive direction matching (F33.3 posture note).
            var fake = new FakeMediaRating { VoteResult = new VoteOutcome(RatingWriteResult.Updated, 51) };
            var controller = new RatingController(fake);

            var result = await controller.Vote(42, new VoteRequest("UP"), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(51, GetProperty<int>(ok.Value, "score"));
            Assert.Equal(("42", VoteDirection.Up), Assert.Single(fake.VoteCalls));
        }

        [Fact]
        public async Task VoteDownReturns200WithThePostVoteScore()
        {
            // {"direction":"down"} → 200 {"score":49}.
            var fake = new FakeMediaRating { VoteResult = new VoteOutcome(RatingWriteResult.Updated, 49) };
            var controller = new RatingController(fake);

            var result = await controller.Vote(42, new VoteRequest("Down"), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(49, GetProperty<int>(ok.Value, "score"));
            Assert.Equal(("42", VoteDirection.Down), Assert.Single(fake.VoteCalls));
        }
    }

    public sealed class ScenarioTogglingNeverPlay
    {
        [Fact]
        public async Task PutTrueReturns200EchoingTheFlag()
        {
            // PUT /api/media/42/never-play {"neverPlay":true} → 200 {"neverPlay":true} (F33.4).
            var fake = new FakeMediaRating { NeverPlayResult = new NeverPlayOutcome(RatingWriteResult.Updated, true) };
            var controller = new RatingController(fake);

            var result = await controller.SetNeverPlay(42, new NeverPlayRequest(true), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True(GetProperty<bool>(ok.Value, "neverPlay"));
            Assert.Equal(("42", true), Assert.Single(fake.NeverPlayCalls));
        }
    }

    public sealed class ScenarioBatchReadingRatings
    {
        [Fact]
        public async Task RatedAndUnratedIdsBothReturnWithDefaults()
        {
            // GET /api/ratings?ids=5,9 (9 unrated) → 200; 9 reads score 50 / neverPlay false
            // (F33.2, F33.9). The repository resolves the defaults — the controller only passes
            // its result through.
            var fake = new FakeMediaRating
            {
                RatingsResult =
                [
                    new MediaRating("5", 73, false),
                    new MediaRating("9", 50, false),
                ],
            };
            var controller = new RatingController(fake);

            var result = await controller.GetRatings("5,9", CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var ratings = Assert.IsAssignableFrom<IReadOnlyList<MediaRating>>(ok.Value);
            Assert.Equal(fake.RatingsResult, ratings);
            Assert.Equal(new[] { "5", "9" }, Assert.Single(fake.RatingsCalls));
        }

        [Fact]
        public async Task NonNumericIdsAreSilentlySkipped()
        {
            // GET /api/ratings?ids=5,tts:abc → 200 with an entry for 5 only (F33.9). The
            // controller passes BOTH ids through unfiltered — it is the repository's job (already
            // proven in MediaLibrary.Tests) to skip tts:abc; here we assert the pass-through
            // contract and that the response reflects whatever the repository resolved.
            var fake = new FakeMediaRating
            {
                RatingsResult = [new MediaRating("5", 60, false)],
            };
            var controller = new RatingController(fake);

            var result = await controller.GetRatings("5,tts:abc", CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var ratings = Assert.IsAssignableFrom<IReadOnlyList<MediaRating>>(ok.Value);
            Assert.Equal(fake.RatingsResult, ratings);
            Assert.Equal(new[] { "5", "tts:abc" }, Assert.Single(fake.RatingsCalls));
        }
    }

    public sealed class ScenarioOutOfMainScopeRowsAreRatable
    {
        [Fact]
        public async Task VoteOnARowOutsideStationScopeSucceeds()
        {
            // A row in a library NOT in Station:Scope:LibraryIds (the seeded safe library shape):
            // POST vote → 200, no 403 — the deliberate F23.4 exception (F33.5, the gitea-#203-trap fix).
            // RatingController takes no IStationScopeProvider dependency at all (see constructor
            // above) — this test proves the request reaches the fake regardless of which id
            // (in-scope or out-of-scope) is voted on.
            var fake = new FakeMediaRating { VoteResult = new VoteOutcome(RatingWriteResult.Updated, 51) };
            var controller = new RatingController(fake);

            var result = await controller.Vote(500, new VoteRequest("up"), CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Single(fake.VoteCalls);
        }

        [Fact]
        public async Task NeverPlayOnARowOutsideStationScopeSucceeds()
        {
            // Same arrange; PUT never-play → 200 (F33.5).
            var fake = new FakeMediaRating { NeverPlayResult = new NeverPlayOutcome(RatingWriteResult.Updated, true) };
            var controller = new RatingController(fake);

            var result = await controller.SetNeverPlay(500, new NeverPlayRequest(true), CancellationToken.None);

            Assert.IsType<OkObjectResult>(result);
            Assert.Single(fake.NeverPlayCalls);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unknown ids, bad bodies, posture
    // ---------------------------------------------------------------------

    public sealed class ScenarioRejectingBadRequests
    {
        [Fact]
        public async Task VoteOnAnUnknownIdReturns404()
        {
            // POST /api/media/999999/vote → 404 (F33.3).
            var fake = new FakeMediaRating { VoteResult = new VoteOutcome(RatingWriteResult.NotFound, null) };
            var controller = new RatingController(fake);

            var result = await controller.Vote(999_999, new VoteRequest("up"), CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task NeverPlayOnAnUnknownIdReturns404()
        {
            // PUT /api/media/999999/never-play → 404 (F33.4).
            var fake = new FakeMediaRating { NeverPlayResult = new NeverPlayOutcome(RatingWriteResult.NotFound, null) };
            var controller = new RatingController(fake);

            var result = await controller.SetNeverPlay(999_999, new NeverPlayRequest(true), CancellationToken.None);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task AnInvalidDirectionReturns400AndWritesNothing()
        {
            // {"direction":"sideways"} → 400 ProblemDetails; the fake records no write (F33.3).
            var fake = new FakeMediaRating();
            var controller = new RatingController(fake);

            var result = await controller.Vote(42, new VoteRequest("sideways"), CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
            Assert.Empty(fake.VoteCalls);
        }
    }

    public sealed class ScenarioDenyByDefaultPosture
    {
        [Fact]
        public async Task AWriteWithoutACookieReturns401()
        {
            // Admin:Password set, no cookie → 401 on POST vote (F18.7 posture).
            await using var factory = new RatingApiWebFactory(withAdminPassword: true);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

            var response = await client.PostAsync(
                "/api/media/1/vote",
                JsonContent.Create(new { direction = "up" }));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AWriteWithoutJsonContentTypeReturns415()
        {
            // Valid cookie, form content-type → 415; nothing written (F18.7 CSRF posture).
            // No Admin:Password set — the factory opens the API so content-type negotiation
            // is tested in isolation, without needing a valid cookie (mirrors Story058).
            await using var factory = new RatingApiWebFactory(withAdminPassword: false);
            var client = factory.CreateClient();

            var body = new StringContent(
                "direction=up",
                System.Text.Encoding.UTF8,
                "application/x-www-form-urlencoded");
            var response = await client.PostAsync("/api/media/1/vote", body);

            // [Consumes("application/json")] returns 415 Unsupported Media Type.
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
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
