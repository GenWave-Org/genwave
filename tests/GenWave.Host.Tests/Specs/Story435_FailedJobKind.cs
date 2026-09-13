// STORY-435 — A failed job says which step failed (API half: AC1–AC5 · gh-#724 · PLAN T462–T464)
// The UI half (AC6–AC8, the wizard routing) lives in admin-ui/__specs__/wizard-failed-step.spec.tsx.
//
// Runner: xUnit through the deployed entry point (WebApplicationFactory<Program> against a real
// ephemeral Postgres — the Story423_WriteJob.cs arc idiom). AC1 runs the REAL write job to a real
// failure (the Story423 over-long-line router route); AC2/AC4 stamp and clear a preview/write job
// through the production IAdSpotStore (the same calls AdSpotJobService makes) because a preview failure
// needs a TTS backend to fall over, which this in-process arc has no honest way to provide. Every
// claim is then read over GET /api/ads/{id}. RED at plan time: ClearJobAsync nulls job_kind and there
// is no job_failed_kind column, so `job.failedKind` is absent from every response.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using GenWave.Ads;
using GenWave.Core.Abstractions;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAFailedJobSaysWhichStepFailed
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(Story435Collection.Name)]
    public sealed class ScenarioAFailedWriteCarriesItsKind(Story435Arc arc)
    {
        [Fact]
        public void TheFailureBecameVisible()
            => Assert.True(arc.FailedWriteBecameVisible, "the failing write never surfaced job.error over GET");

        [Fact]
        public void FailedKindIsWrite()
            => Assert.Equal("write", arc.FailedWriteFailedKind);

        [Fact]
        public void KindStaysNull()
            => Assert.Null(arc.FailedWriteKind);
    }

    [Collection(Story435Collection.Name)]
    public sealed class ScenarioAFailedPreviewCarriesItsKind(Story435Arc arc)
    {
        [Fact]
        public void FailedKindIsPreview()
            => Assert.Equal("preview", arc.FailedPreviewFailedKind);

        [Fact]
        public void TheErrorRidesAlong()
            => Assert.Equal("tts_timeout", arc.FailedPreviewError);
    }

    [Collection(Story435Collection.Name)]
    public sealed class ScenarioANewJobClearsTheFailedKind(Story435Arc arc)
    {
        [Fact]
        public void TheFailedKindColumnExists()
            => Assert.True(arc.FailedKindColumnReadable, "station.ad_spot has no job_failed_kind column (db/47 + the db/06 mirror)");

        [Fact]
        public void TheStoredFailedKindIsNullWhileTheNewJobRuns()
            => Assert.Null(arc.FailedKindWhileRestamped);
    }

    [Collection(Story435Collection.Name)]
    public sealed class ScenarioASuccessfulJobCarriesNoFailedKind(Story435Arc arc)
    {
        [Fact]
        public void TheSuccessSettled()
            => Assert.True(arc.SuccessSettled, "the successful write never cleared its own job stamp");

        [Fact]
        public void JobIsNull()
            => Assert.False(arc.SuccessJobIsPresent);
    }

    // ── PLAN T462 — the job_failed_kind CHECK predicate is text-pinned, identically, in BOTH
    // db/47 (the in-place ALTER) and db/06 (the fresh-init mirror) — the gh-#618 lesson (see either
    // file's own header). No DB needed, so this scenario sits outside Story435Collection.

    public sealed class ScenarioTheFailedKindColumnIsMirroredInFreshInit
    {
        const string FailedKindCheckPredicate =
            "job_failed_kind IS NULL OR job_failed_kind IN ('write', 'preview')";

        [Fact]
        public void Db47CarriesTheCheckPredicate()
        {
            var db47 = File.ReadAllText(
                Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "db", "47-ad-spot-job-failed-kind-migration.sh"));
            Assert.Contains(FailedKindCheckPredicate, db47);
        }

        [Fact]
        public void Db06CarriesTheIdenticalCheckPredicate()
        {
            // The fresh-init mirror must define the SAME predicate as db/47's own ALTER — a fresh
            // install and an upgraded box must enforce job_failed_kind identically.
            var db06 = File.ReadAllText(
                Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "db", "06-station-settings-migration.sh"));
            Assert.Contains(FailedKindCheckPredicate, db06);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class Story435Collection : ICollectionFixture<Story435Arc>
{
    public const string Name = "Story435FailedJobKind";
}

public sealed class Story435Arc : IAsyncLifetime
{
    public bool FailedWriteBecameVisible { get; private set; }
    public string? FailedWriteFailedKind { get; private set; }
    public string? FailedWriteKind { get; private set; }

    public string? FailedPreviewFailedKind { get; private set; }
    public string? FailedPreviewError { get; private set; }

    public bool FailedKindColumnReadable { get; private set; }
    public string? FailedKindWhileRestamped { get; private set; } = "unread";

    public bool SuccessSettled { get; private set; }
    public bool SuccessJobIsPresent { get; private set; } = true;

    public async Task InitializeAsync()
    {
        await using var database = await Story435Database.StartAsync();
        var router = new AdScriptCompletionsRouter();
        await using var factory = new Story435WebFactory(database, router);
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Story435WebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        var store = factory.Services.GetRequiredService<IAdSpotStore>();

        // ── AC1/AC3 — a REAL write failure (the Story423 AC4 route: one 900-char line every ask). ──
        const string failingSponsorName = "Ferrous Finch Ironworks";
        var overLongLine = "ANNOUNCER: " + new string('A', 900);
        router.RouteSponsor(failingSponsorName, _ =>
            Task.FromResult(AdScriptCompletionsRouter.CompletionsResponse(overLongLine)));
        var failingSponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, failingSponsorName);
        var failingSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(
            client, failingSponsorId, "Failing write", "A short deal whose writer call fails outright.");
        var enqueueFailing = await AdSpotJobTestHelpers.PostWriteAsync(client, failingSpotId);
        if (enqueueFailing.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException($"arrange: POST /api/ads/{failingSpotId}/write unexpectedly returned {enqueueFailing.StatusCode}");

        var (visible, failedBody) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, failingSpotId,
            body => AdSpotJobTestHelpers.JobIsPresent(body) && AdSpotJobTestHelpers.JobError(body) is not null,
            TimeSpan.FromSeconds(10));
        FailedWriteBecameVisible = visible;
        FailedWriteKind = AdSpotJobTestHelpers.JobKind(failedBody);
        FailedWriteFailedKind = FailedKind(failedBody);

        // ── AC4 — a fresh write stamp on that same failed row wipes the stored failed kind. Stamped
        // through the production store (the exact call AdSpotJobService.TryEnqueueAsync makes) so the
        // row is held in the in-flight shape long enough to read; cleared again afterwards. ──
        var stamp = await store.StampJobAsync(failingSpotId, "write", CancellationToken.None);
        try
        {
            (FailedKindColumnReadable, FailedKindWhileRestamped) =
                await TryReadFailedKindAsync(database.StationConnectionString, failingSpotId);
        }
        finally
        {
            await store.ClearJobAsync(failingSpotId, error: null, CancellationToken.None);
        }
        _ = stamp;

        // ── AC2 — a preview failure, stamped and cleared through the production store. ──
        var sponsorId = await AdSpotJobTestHelpers.CreateSponsorAsync(client, "Bramble & Byte Repairs");
        var (previewSpotId, _) = await AdSpotJobTestHelpers.CreateDraftSpotWithScriptAsync(
            client, sponsorId, "Failing preview",
            "ANNOUNCER: Bramble & Byte Repairs has a deal so good it's almost illegal.\nANNOUNCER: Call 555-0199 - that's 555-0199 - Bramble & Byte.");
        await store.StampJobAsync(previewSpotId, "preview", CancellationToken.None);
        await store.ClearJobAsync(previewSpotId, error: "tts_timeout", CancellationToken.None);
        var previewBody = await AdSpotJobTestHelpers.GetSpotAsync(client, previewSpotId);
        FailedPreviewFailedKind = FailedKind(previewBody);
        FailedPreviewError = AdSpotJobTestHelpers.JobError(previewBody);

        // ── AC5 — a write that succeeds leaves job: null (no failedKind anywhere). ──
        var successSpotId = await AdSpotJobTestHelpers.CreateDraftSpotAsync(
            client, sponsorId, "Success spot", "A short deal that writes cleanly.");
        var enqueueSuccess = await AdSpotJobTestHelpers.PostWriteAsync(client, successSpotId);
        if (enqueueSuccess.StatusCode != HttpStatusCode.Accepted)
            throw new InvalidOperationException($"arrange: POST /api/ads/{successSpotId}/write unexpectedly returned {enqueueSuccess.StatusCode}");
        var (settled, settledBody) = await AdSpotJobTestHelpers.TryPollUntilAsync(
            client, successSpotId, AdSpotJobTestHelpers.JobIsSettled, TimeSpan.FromSeconds(10));
        SuccessSettled = settled;
        SuccessJobIsPresent = AdSpotJobTestHelpers.JobIsPresent(settledBody);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    static string? FailedKind(JsonElement body) =>
        body.GetProperty("job") is { ValueKind: JsonValueKind.Object } job &&
            job.TryGetProperty("failedKind", out var kind) && kind.ValueKind == JsonValueKind.String
            ? kind.GetString()
            : null;

    /// <summary>Reads <c>job_failed_kind</c> off the row; a missing column (db/47 not yet applied)
    /// comes back as (false, null) instead of crashing every fact in the collection.</summary>
    static async Task<(bool Readable, string? Value)> TryReadFailedKindAsync(string stationConnectionString, long spotId)
    {
        await using var conn = new NpgsqlConnection(stationConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select job_failed_kind from station.ad_spot where id = @id";
        cmd.Parameters.AddWithValue("id", spotId);
        try
        {
            var value = await cmd.ExecuteScalarAsync();
            return (true, value is DBNull or null ? null : (string)value);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            return (false, null);
        }
    }
}

file sealed class Story435WebFactory(EphemeralStationDatabase db, AdScriptCompletionsRouter router)
    : WebApplicationFactory<Program>
{
    public const string Password = "test-password-t464-failed-kind";

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
        builder.UseSetting("Ads:JobQueueCapacity", "4");
        builder.UseSetting("Llm:Endpoint", "http://fake-llm.local");
        builder.UseSetting("Llm:Model", "test-model");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<AdSpotJobService>());

            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(router.Handler));
        });
    }
}

file sealed class Story435Database : EphemeralStationDatabase
{
    Story435Database(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story435Database> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t464c");
        var db = new Story435Database(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}
