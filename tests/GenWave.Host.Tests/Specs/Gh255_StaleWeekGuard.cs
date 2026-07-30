// gh-#255 — schedule: a block spanning the whole week must save, round-trip, and never be lost to
// a stale editor's full-replace.
//
// BDD specification — xUnit. Entry-point discipline mirrors Story240_GridHoldsTheWeek.cs: every
// scenario drives GET/PUT /api/schedule through WebApplicationFactory<Program> with real cookie
// auth, against FakeScheduleStore (whose expectedVersion handling mirrors ScheduleRepository's own
// guard, itself proven against real Postgres in MediaLibrary.Tests). Two halves:
//
//   1. The literal issue repro pinned green at the wire: a 2h block across all 7 days — and a
//      7×full-day whole-week block — PUTs one segment per day, 200s, and round-trips identically
//      through the immediately-following GET.
//   2. The demonstrated loss vector closed: PUT is a blind full-replace, so a stale editor
//      (second tab, long-lived session — observed live on the demo box as segmentCount 54 → 48
//      with no error anywhere) must get a 409 with the "staleWeek" marker instead of silently
//      wiping the newer week. baseVersion: null keeps legacy behavior.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

/// <summary>Same shape as Story240's own factory (which is file-scoped there): the real Program.cs
/// graph with <see cref="IScheduleStore"/> swapped for the fake.</summary>
file sealed class StaleWeekApiWebFactory(FakeScheduleStore store) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-stale-week-guard";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IScheduleStore>();
            services.AddSingleton<IScheduleStore>(store);
        });
    }

    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

public static class FeatureStaleWeekGuard
{
    static ScheduleSegmentDto Staffed(int day, int start, int end, long personaId) =>
        new(Id: null, day, start, end, personaId, Genres: null, EnergyMin: null, EnergyMax: null);

    /// <summary>The gh-#255 repro: one block, every day of the week.</summary>
    static ScheduleWeekDto FullWeekBand(long personaId, string? baseVersion = null) =>
        new(
            Enumerable.Range(0, 7).Select(day => Staffed(day, 600, 720, personaId)).ToArray(),
            Version: null,
            BaseVersion: baseVersion);

    static ScheduleSegment StoredSegment(long id, int day, int start, int end, long personaId) =>
        new(id, (DayOfWeek)day, start, end, personaId, Genres: null, EnergyMin: null, EnergyMax: null);

    public sealed class ScenarioAWholeWeekBlockSavesAndRoundTrips
    {
        [Fact]
        public async Task ATwoHourBlockAcrossAllSevenDaysPutsOneSegmentPerDayAndComesBackIdentical()
        {
            var store = new FakeScheduleStore();
            await using var factory = new StaleWeekApiWebFactory(store);
            var client = await StaleWeekApiWebFactory.LoggedInClientAsync(factory);

            var put = await client.PutAsJsonAsync("/api/schedule", FullWeekBand(personaId: 7));
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var putBody = await put.Content.ReadFromJsonAsync<ScheduleWeekDto>();

            var get = await client.GetAsync("/api/schedule");
            var getBody = await get.Content.ReadFromJsonAsync<ScheduleWeekDto>();

            Assert.NotNull(putBody);
            Assert.NotNull(getBody);
            Assert.Equal(7, putBody.Segments.Count);
            Assert.Equal(7, getBody.Segments.Count);
            for (var day = 0; day < 7; day++)
            {
                var fromGet = getBody.Segments.Single(s => s.Day == day);
                Assert.Equal(600, fromGet.StartMinute);
                Assert.Equal(720, fromGet.EndMinute);
                Assert.Equal(7, fromGet.PersonaId);
            }
        }

        [Fact]
        public async Task SevenFullDayRowsTheWholeWeekAsOneBlockRoundTripToo()
        {
            var store = new FakeScheduleStore();
            await using var factory = new StaleWeekApiWebFactory(store);
            var client = await StaleWeekApiWebFactory.LoggedInClientAsync(factory);

            var request = new ScheduleWeekDto(
                Enumerable.Range(0, 7).Select(day => Staffed(day, 0, 1440, 7)).ToArray());

            var put = await client.PutAsJsonAsync("/api/schedule", request);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            var getBody = await (await client.GetAsync("/api/schedule")).Content.ReadFromJsonAsync<ScheduleWeekDto>();
            Assert.NotNull(getBody);
            Assert.Equal(7, getBody.Segments.Count);
            Assert.All(getBody.Segments, s =>
            {
                Assert.Equal(0, s.StartMinute);
                Assert.Equal(1440, s.EndMinute);
                Assert.Equal(7, s.PersonaId);
            });
        }
    }

    public sealed class ScenarioVersionTravelsTheWire
    {
        [Fact]
        public async Task GetCarriesTheStoredWeeksContentFingerprint()
        {
            var stored = new ScheduleWeekSnapshot([StoredSegment(1, 1, 0, 600, 7)]);
            var store = new FakeScheduleStore(stored);
            await using var factory = new StaleWeekApiWebFactory(store);
            var client = await StaleWeekApiWebFactory.LoggedInClientAsync(factory);

            var getBody = await (await client.GetAsync("/api/schedule")).Content.ReadFromJsonAsync<ScheduleWeekDto>();

            Assert.NotNull(getBody);
            Assert.Equal(ScheduleWeekVersion.Compute(stored.Segments), getBody.Version);
        }

        [Fact]
        public async Task ASuccessfulPutReturnsTheFreshWeeksVersion()
        {
            var store = new FakeScheduleStore();
            await using var factory = new StaleWeekApiWebFactory(store);
            var client = await StaleWeekApiWebFactory.LoggedInClientAsync(factory);

            var put = await client.PutAsJsonAsync("/api/schedule", FullWeekBand(personaId: 7));
            var putBody = await put.Content.ReadFromJsonAsync<ScheduleWeekDto>();

            Assert.NotNull(putBody);
            Assert.False(string.IsNullOrEmpty(putBody.Version));
            // The version describes the JUST-WRITTEN week — a client saving again unchanged with it
            // must pass the guard, so it must equal the stored snapshot's own fingerprint.
            var current = await store.LoadWeekAsync(CancellationToken.None);
            Assert.Equal(ScheduleWeekVersion.Compute(current.Segments), putBody.Version);
        }
    }

    public sealed class ScenarioAStaleEditorCannotWipeTheWeek
    {
        [Fact]
        public async Task APutCarryingAnOutdatedBaseVersionIs409StaleWeekAndChangesNothing()
        {
            // The stored week already holds a newer save (the demo's 54-segment moment, scaled
            // down); the submitting editor loaded BEFORE that save, so its baseVersion is stale.
            var newerWeek = new ScheduleWeekSnapshot([StoredSegment(1, 2, 0, 600, 9)]);
            var staleVersion = ScheduleWeekVersion.Compute([]); // what the editor loaded: an empty week.
            var store = new FakeScheduleStore(newerWeek);
            await using var factory = new StaleWeekApiWebFactory(store);
            var client = await StaleWeekApiWebFactory.LoggedInClientAsync(factory);

            var put = await client.PutAsJsonAsync("/api/schedule", FullWeekBand(personaId: 7, staleVersion));

            Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
            var problem = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
            Assert.Equal("staleWeek", problem.RootElement.GetProperty("conflict").GetString());

            // Nothing was written — the newer week survives untouched.
            var current = await store.LoadWeekAsync(CancellationToken.None);
            var survivor = Assert.Single(current.Segments);
            Assert.Equal(9, survivor.PersonaId);
        }

        [Fact]
        public async Task APutCarryingTheCurrentVersionSucceeds()
        {
            var stored = new ScheduleWeekSnapshot([StoredSegment(1, 2, 0, 600, 9)]);
            var store = new FakeScheduleStore(stored);
            await using var factory = new StaleWeekApiWebFactory(store);
            var client = await StaleWeekApiWebFactory.LoggedInClientAsync(factory);

            var put = await client.PutAsJsonAsync(
                "/api/schedule", FullWeekBand(personaId: 7, ScheduleWeekVersion.Compute(stored.Segments)));

            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        [Fact]
        public async Task ANullBaseVersionSkipsTheGuardEntirely()
        {
            // Legacy-client posture: no baseVersion, no check — the pre-guard behavior unchanged.
            var stored = new ScheduleWeekSnapshot([StoredSegment(1, 2, 0, 600, 9)]);
            var store = new FakeScheduleStore(stored);
            await using var factory = new StaleWeekApiWebFactory(store);
            var client = await StaleWeekApiWebFactory.LoggedInClientAsync(factory);

            var put = await client.PutAsJsonAsync("/api/schedule", FullWeekBand(personaId: 7));

            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }
    }
}
