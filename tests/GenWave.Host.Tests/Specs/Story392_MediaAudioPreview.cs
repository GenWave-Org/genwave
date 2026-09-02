// STORY-392 AC4 — a ready spot's rendered artifact plays in the browser (PLAN T404b)
//
// PLAN T404b split off T404 (2026-09-02): STORY-392 AC4's preview was structurally unfulfillable —
// no endpoint served persisted `library.media` bytes to the admin UI at all. This is the byte route:
// GET /api/media/{id}/audio (MediaController.GetAudio).
//
// BDD specification — xUnit through the deployed entry point (WebApplicationFactory<Program> against
// a real ephemeral Postgres AND a real wav file on disk — the Story392_AdsApi/EphemeralStationDatabase
// idiom, extended here with an actual byte stream rather than a metadata-only row): every fact drives
// GET /api/media/{id}/audio over real HTTP with an authed admin session, never MediaController/
// IAdminMediaLookup directly. One arc (MediaAudioPreviewArc) arranges everything every HAPPY-PATH/
// sad-path Scenario below reads (the "arrange once, many read-only Scenarios" idiom); the admin-off
// and public-listener postures need no real database at all (SurfaceGateMiddleware 404s on endpoint
// metadata alone, before any store is ever touched — the Story392.AdsAdminOffWebFactory precedent),
// so they get their own DB-less factories.
//
// The public-listener fact mirrors Story172_PublicListenerIsolation's own simulated-port idiom
// (TestServer opens no real sockets, so SimulatedPortStartupFilter stamps Connection.LocalPort to
// fake arrival on the public port) — this route is AdminSurface, never SpectatorSurface, so SPEC F64
// demands it 404 there regardless of any id or Admin:Enabled value.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureMediaAudioPreview
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — through the production surface (WebApplicationFactory)
    // ---------------------------------------------------------------------

    [Collection(MediaAudioPreviewCollection.Name)]
    public sealed class ScenarioAReadyRowStreamsItsRealBytes(MediaAudioPreviewArc arc)
    {
        [Fact]
        public void AFullGetReturns200WithTheFormatMappedContentType()
        {
            Assert.Equal(HttpStatusCode.OK, arc.FullGetStatus);
            Assert.Equal("audio/wav", arc.FullGetContentType);
        }

        [Fact]
        public void AFullGetAdvertisesByteRangeSupport()
        {
            Assert.Contains("bytes", arc.FullGetAcceptRanges);
        }

        [Fact]
        public void AFullGetReturnsTheExactBytesOnDisk()
        {
            Assert.Equal(arc.WavBytes, arc.FullGetBytes);
        }

        [Fact]
        public void ARangeRequestReturns206PartialContentWithOnlyTheRequestedSlice()
        {
            Assert.Equal(HttpStatusCode.PartialContent, arc.RangeStatus);
            Assert.Equal(arc.WavBytes[2..6], arc.RangeBytes);
        }
    }

    // ---------------------------------------------------------------------
    // CACHING POSTURE — stamped explicitly by the action, not left to NoCacheApiMiddleware's own
    // !Response.HasStarted best-effort (the review blocker: a streamed body has already started by
    // the time that middleware runs, so only a bodyless HEAD kept its stamp — one route, two
    // postures). GET/206/HEAD parity is the whole point, so all three get their own fact.
    // ---------------------------------------------------------------------

    [Collection(MediaAudioPreviewCollection.Name)]
    public sealed class ScenarioTheResponseIsNeverCached(MediaAudioPreviewArc arc)
    {
        [Fact]
        public void AFullGetCarriesNoStoreAndNosniffExplicitly()
        {
            Assert.Equal("no-store", arc.FullGetCacheControl);
            Assert.Equal("nosniff", arc.FullGetXContentTypeOptions);
        }

        [Fact]
        public void ARangeResponseCarriesNoStore()
        {
            Assert.Equal("no-store", arc.RangeCacheControl);
        }

        [Fact]
        public void AHeadResponseCarriesNoStoreTheSameAsTheGet()
        {
            // The GET/HEAD parity the middleware-only posture could never deliver (both now come
            // from the SAME explicit stamp in MediaController.GetAudio, not two different sources).
            Assert.Equal(HttpStatusCode.OK, arc.HeadStatus);
            Assert.Equal("no-store", arc.HeadCacheControl);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no data disclosed, no 500 on a filesystem miss
    // ---------------------------------------------------------------------

    [Collection(MediaAudioPreviewCollection.Name)]
    public sealed class ScenarioUnknownIdIsAnHonest404(MediaAudioPreviewArc arc)
    {
        [Fact]
        public void UnknownIdReturns404()
        {
            Assert.Equal(HttpStatusCode.NotFound, arc.UnknownIdStatus);
        }
    }

    [Collection(MediaAudioPreviewCollection.Name)]
    public sealed class ScenarioADeadFileIs404NotA500(MediaAudioPreviewArc arc)
    {
        [Fact]
        public void ARowWhoseFileHasVanishedFromDiskReturns404()
        {
            // The dead-file class (PLAN T404b's own posture — the Gardener owns dead-file
            // VISIBILITY as an operator-facing signal; this route only refuses to 500 on it).
            Assert.Equal(HttpStatusCode.NotFound, arc.MissingFileStatus);
        }
    }

    public sealed class ScenarioAdminOff
    {
        [Fact]
        public async Task RouteReturns404WhenAdminIsDisabled()
        {
            // No real Postgres needed — Admin:Enabled=false 404s in SurfaceGateMiddleware, before
            // routing ever reaches MediaController's constructor (the Story392.AdsAdminOffWebFactory
            // precedent).
            await using var factory = new MediaAudioAdminOffWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/api/media/1/audio");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    public sealed class ScenarioPublicListenerIsolation
    {
        [Fact]
        public async Task RouteDoesNotExistOnThePublicListener()
        {
            // SPEC F64 — MediaController is [AdminSurface], never [SpectatorSurface]; a request that
            // ARRIVES on the dedicated public port must 404 regardless of id or Admin:Enabled, so a
            // fronting-proxy misroute onto the public port can never leak a byte (Story172's posture).
            await using var factory = new MediaAudioPublicListenerWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/media/1/audio");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    public sealed class ScenarioTheCurationPolicyGatesIt
    {
        [Fact]
        public async Task AnUnauthenticatedRequestIsRefusedWhenAdminIsOn()
        {
            // Pins [Authorize(Policy = AuthorizationPolicies.Curation)] on THIS action (the same
            // policy GET /api/media/{id} carries — the task's own "mirror it" instruction): with
            // Admin:Password set (admin ON) but no session cookie sent, the deny-by-default policy
            // must refuse before MediaController is ever reached — no real Postgres needed, since
            // auth runs before the controller's constructor either way.
            await using var factory = new MediaAudioUnauthenticatedWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.GetAsync("/api/media/1/audio");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}

// ── Collection definition — one ephemeral Postgres/factory/on-disk wav shared by every happy-path/
// sad-path Scenario above (the Story392_AdsApi "arrange once, many read-only Scenarios" idiom). ──

[CollectionDefinition(Name)]
public sealed class MediaAudioPreviewCollection : ICollectionFixture<MediaAudioPreviewArc>
{
    public const string Name = "T404bMediaAudioPreview";
}

/// <summary>
/// Arranges every fact this file's Scenarios read, entirely over the REAL production HTTP pipeline
/// with a real admin session and a real wav file on disk — no MediaController/IAdminMediaLookup call
/// anywhere in this class. Two <c>library.media</c> rows are seeded directly via raw SQL (the
/// AdsWireFixtures precedent): one whose <c>path</c> points at a real file this arc writes to a temp
/// directory, and one whose <c>path</c> points at nothing (the dead-file class).
/// </summary>
public sealed class MediaAudioPreviewArc : IAsyncLifetime
{
    readonly string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public byte[] WavBytes { get; private set; } = [];

    public HttpStatusCode FullGetStatus { get; private set; }
    public string? FullGetContentType { get; private set; }
    public IReadOnlyCollection<string> FullGetAcceptRanges { get; private set; } = [];
    public byte[] FullGetBytes { get; private set; } = [];
    public string? FullGetCacheControl { get; private set; }
    public string? FullGetXContentTypeOptions { get; private set; }

    public HttpStatusCode RangeStatus { get; private set; }
    public byte[] RangeBytes { get; private set; } = [];
    public string? RangeCacheControl { get; private set; }

    public HttpStatusCode HeadStatus { get; private set; }
    public string? HeadCacheControl { get; private set; }

    public HttpStatusCode UnknownIdStatus { get; private set; }
    public HttpStatusCode MissingFileStatus { get; private set; }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(tempDir);
        var wavPath = Path.Combine(tempDir, "t404b-spot.wav");
        WavBytes = BuildMinimalWav();
        await File.WriteAllBytesAsync(wavPath, WavBytes);
        var missingPath = Path.Combine(tempDir, "does-not-exist.wav");

        // A LOCAL, not a field — MediaAudioPreviewDatabase is file-local (CS9051), the identical
        // reason Story392_AdsApi's own arc gives for the same shape.
        await using var database = await MediaAudioPreviewDatabase.StartAsync();

        var readyId = await MediaAudioWireFixtures.InsertMediaRowAsync(database.LibraryConnectionString, wavPath, "wav");
        var missingFileId = await MediaAudioWireFixtures.InsertMediaRowAsync(database.LibraryConnectionString, missingPath, "wav");

        await using var factory = new MediaAudioPreviewWebFactory(database);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = MediaAudioPreviewWebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var fullResponse = await client.GetAsync($"/api/media/{readyId}/audio");
        FullGetStatus = fullResponse.StatusCode;
        FullGetContentType = fullResponse.Content.Headers.ContentType?.MediaType;
        FullGetAcceptRanges = fullResponse.Headers.AcceptRanges.ToArray();
        FullGetBytes = await fullResponse.Content.ReadAsByteArrayAsync();
        FullGetCacheControl = fullResponse.Headers.CacheControl?.ToString();
        FullGetXContentTypeOptions = fullResponse.Headers.TryGetValues("X-Content-Type-Options", out var xcto)
            ? xcto.FirstOrDefault() : null;

        var rangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/media/{readyId}/audio");
        rangeRequest.Headers.Range = new RangeHeaderValue(2, 5);
        var rangeResponse = await client.SendAsync(rangeRequest);
        RangeStatus = rangeResponse.StatusCode;
        RangeBytes = await rangeResponse.Content.ReadAsByteArrayAsync();
        RangeCacheControl = rangeResponse.Headers.CacheControl?.ToString();

        var headRequest = new HttpRequestMessage(HttpMethod.Head, $"/api/media/{readyId}/audio");
        var headResponse = await client.SendAsync(headRequest);
        HeadStatus = headResponse.StatusCode;
        HeadCacheControl = headResponse.Headers.CacheControl?.ToString();

        var unknownResponse = await client.GetAsync("/api/media/999999999/audio");
        UnknownIdStatus = unknownResponse.StatusCode;

        var missingFileResponse = await client.GetAsync($"/api/media/{missingFileId}/audio");
        MissingFileStatus = missingFileResponse.StatusCode;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>A genuinely parseable RIFF/WAVE file (mono, 16-bit PCM, 44.1kHz, 10 silent samples)
    /// — not a byte-count placeholder — long enough (64 bytes) for a meaningful Range slice.</summary>
    static byte[] BuildMinimalWav()
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;
        var samples = new short[10];
        var dataBytes = samples.Length * sizeof(short);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write("data"u8.ToArray());
            writer.Write(dataBytes);
            foreach (var sample in samples)
                writer.Write(sample);
        }

        return stream.ToArray();
    }
}

// ── Test harness — WebApplicationFactory + ephemeral Postgres subclasses (the Story392_AdsApi
// "`file`-scoped types cannot cross files" precedent — this file supplies its own). ──

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres with every hosted
/// service removed — no background reach into this arc's own seeded rows, mirroring
/// Story392_AdsApi.Story392AdsWebFactory exactly.
/// </summary>
file sealed class MediaAudioPreviewWebFactory(MediaAudioPreviewDatabase db) : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t404b-media-audio";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// STORY-392 AC6's own DB-less posture, extended to T404b's byte route: a bogus
/// <c>ConnectionStrings:*</c> (never actually reached — <c>Admin:Enabled=false</c> 404s in
/// <c>SurfaceGateMiddleware</c> before routing ever reaches <c>MediaController</c>'s constructor),
/// mirroring <c>Story392_AdsApi.AdsAdminOffWebFactory</c>.
/// </summary>
file sealed class MediaAudioAdminOffWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// LOW-2 (T404b review) — pins the <see cref="AuthorizationPolicies.Curation"/> policy on
/// <c>MediaController.GetAudio</c> itself: <c>Admin:Password</c> IS set (admin on — unlike
/// <see cref="MediaAudioAdminOffWebFactory"/>), so the surface exists, but no request in this
/// factory's own scenario ever logs in — the deny-by-default auth gate must refuse it before
/// <c>MediaController</c> is ever constructed. No real Postgres needed for the same reason.
/// </summary>
file sealed class MediaAudioUnauthenticatedWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Admin:Password", "test-password-t404b-unauthenticated");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}

/// <summary>
/// Simulates a request arriving on the dedicated public listener port (Story172's own idiom — no
/// real socket is ever opened by TestServer, so <see cref="SimulatedPortStartupFilter"/> stamps
/// <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.LocalPort"/> instead) — no real Postgres
/// needed, since <c>SurfaceGateMiddleware</c>'s public-listener check runs on endpoint metadata alone,
/// before <c>MediaController</c> is ever constructed.
/// </summary>
file sealed class MediaAudioPublicListenerWebFactory : WebApplicationFactory<Program>
{
    internal const int PublicPort = 8082;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Spectator:PublicPort", PublicPort.ToString());
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(
                new SimulatedPortStartupFilter(PublicPort));
        });
    }
}

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness — see
/// that type's own remarks for the full "which compose file, why a unique project name + OS-assigned
/// port" rationale. Supplies only the <c>"genwave-t404b"</c> compose project-name prefix this file's
/// own arc needs.
/// </summary>
file sealed class MediaAudioPreviewDatabase : EphemeralStationDatabase
{
    MediaAudioPreviewDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<MediaAudioPreviewDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t404b");
        var db = new MediaAudioPreviewDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>Arrange helper this file's own arc uses — raw SQL against the ephemeral database's own
/// Library connection, never through <c>IAdminMediaLookup</c> (the AdsWireFixtures precedent):
/// <c>format</c> is parameterized (unlike AdsWireFixtures.InsertPlayableMediaRowAsync's hardcoded
/// 'flac') so this file can seed the 'wav' row its own content-type fact needs, and the row's
/// <c>path</c> may point at a file that does or doesn't actually exist on disk — the caller decides
/// which fact it is arranging.</summary>
file static class MediaAudioWireFixtures
{
    public static async Task<long> InsertMediaRowAsync(
        string libraryConnectionString, string path, string format)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (path, format, size_bytes, mtime, state, duration_ms, title, artist, eligible)
            values (@path, @format, 1024, now(), 'ready', 1000, 'T404b probe', 'Test Artist', true)
            returning id
            """;
        cmd.Parameters.AddWithValue("path", path);
        cmd.Parameters.AddWithValue("format", format);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }
}
