// STORY-215 — The persona learns only from me, and can't spiral
//
// BDD specification — xUnit (SPEC F84.1–F84.7). PLAN T60 stamps the booth log; T70 wires the
// thumb endpoint WITH every guardrail in the same change (F84.4 — no commit has accrual without
// brakes).
//
// Most facts below drive the REAL BoothLogController.ThumbTaste action directly (constructed with a
// scriptable, REPLICATING IPersonaTasteAccrualStore fake — mirrors Story112_RatingEndpoints.cs's own
// "direct controller construction, no WebApplicationFactory" idiom for RatingController, the closest
// sibling endpoint in this codebase). FakePersonaTasteAccrualStore does not merely record calls — it
// replicates the real repository's own nudge/clamp/cap-eviction/ledger-dedup semantics in memory
// (mirrors FakePersonaImportStore in Story209_PersonaImport.cs), so a fact here is a fact about the
// CONTROLLER's request/response shape, never an artifact of a fake that happens to do nothing.
//
// The two F84.7 disjointness facts drive the real HTTP pipeline via WebApplicationFactory<Program>
// instead: they need ONE shared DI container serving BOTH the real POST /api/booth-log/{id}/taste-thumb
// and POST /api/media/{id}/vote routes, with recording fakes bound at the container level, so a
// regression that accidentally wired one controller's store into the other's request path would
// actually be caught — a fake constructed but never injected into the other controller would not be.
//
// The 500-pick zero-input simulation drives the REAL GenWave.Orchestration.PersonaRanker (no fake
// scoring/sampling logic) over an in-memory candidate pool with a seeded RNG — deterministic, no I/O.
//
// Repository-level facts (the nudge transaction, cap-eviction, ledger durability/concurrency against
// REAL Postgres) live in GenWave.MediaLibrary.Tests/Specs/Story215_TasteAccrualRepository.cs instead,
// per the T60/T69 split precedent this file's own header already documents for BoothLogPersonaStamp:
// Host.Tests has no station-Postgres convention.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Orchestration;

using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Fixed single-page <see cref="IBoothLogReader"/> double (STORY-215, T60) — this file only needs to
/// prove the controller/DTO projects a row's persona stamp through unchanged; the real keyset-paging
/// behavior is Story195_BoothLog.cs's own concern (its own <c>FakeBoothLogReader</c> is file-scoped
/// there, so this file scripts its own minimal double rather than reaching across files). The thumb
/// facts below never call <c>List</c> at all — <see cref="BoothLogController.ThumbTaste"/> depends on
/// <see cref="IPersonaTasteAccrualStore"/> only — so an empty page is all any of them need.
/// </summary>
file sealed class FixedPageBoothLogReader(BoothLogPage page) : IBoothLogReader
{
    public Task<BoothLogPage> ReadAsync(BoothLogCursor? before, int take, CancellationToken ct) =>
        Task.FromResult(page);

    public Task<long?> GetMediaIdAsync(long id, CancellationToken ct) =>
        Task.FromResult(page.Entries.FirstOrDefault(e => e.Id == id)?.MediaId);
}

/// <summary>One thumbable (or not) booth-log row, as <see cref="FakePersonaTasteAccrualStore"/> sees it.</summary>
file sealed class FakeBoothLogRow
{
    public required long Id { get; init; }
    public string Kind { get; init; } = "track-started";
    public long? PersonaId { get; init; }
    public string? Artist { get; init; }
}

/// <summary>
/// Scriptable <see cref="IPersonaTasteAccrualStore"/> double that REPLICATES
/// <c>PersonaTasteAccrualRepository</c>'s own rules in memory: attribution comes from the row's OWN
/// stamp (never a notional "active now" persona), a thumb is idempotent per (persona, row, direction)
/// via <see cref="ledger"/>, a nudge steps ±0.2 clamped to <c>[-1, 1]</c>, and the accrued-only cap-50
/// eviction runs right after every nudge. <see cref="ThumbCalls"/> records every request this store
/// ever saw, so a disjointness scenario can assert it was never even reached.
/// </summary>
file sealed class FakePersonaTasteAccrualStore : IPersonaTasteAccrualStore
{
    const double Step = 0.2;
    const int Cap = 50;

    public List<FakeBoothLogRow> Rows { get; } = [];
    public List<PersonaTasteEntry> TasteRows { get; } = [];
    public List<(long BoothLogId, TasteThumbDirection Direction)> ThumbCalls { get; } = [];

    readonly HashSet<(long PersonaId, long BoothLogId, TasteThumbDirection Direction)> ledger = [];
    long nextTasteId = 1;

    public Task<TasteThumbOutcome> ThumbAsync(long boothLogId, TasteThumbDirection direction, CancellationToken ct)
    {
        ThumbCalls.Add((boothLogId, direction));

        var row = Rows.FirstOrDefault(r => r.Id == boothLogId);
        if (row is null)
            return Task.FromResult<TasteThumbOutcome>(new TasteThumbOutcome.RowNotFound());

        if (row.Kind != "track-started" || row.PersonaId is not long personaId || string.IsNullOrWhiteSpace(row.Artist))
            return Task.FromResult<TasteThumbOutcome>(new TasteThumbOutcome.NotThumbable());

        if (!ledger.Add((personaId, boothLogId, direction)))
            return Task.FromResult<TasteThumbOutcome>(new TasteThumbOutcome.AlreadyRecorded(personaId));

        // Predicate identity only (never Context — TasteContext.DaysOfWeek is an IReadOnlyList<DayOfWeek>,
        // an interface-typed member record equality compares by reference, the same gotcha
        // Story209_PersonaImport.cs's own ScenarioReImportOntoALivingPersona documents; every accrued
        // rule here uses the identical no-gate context, so matching on the artist predicate alone is
        // both sufficient and reference-equality-safe).
        var predicate = new TastePredicate(row.Artist, null, null);
        var existing = TasteRows.FirstOrDefault(e =>
            e.PersonaId == personaId && e.Source == PersonaTasteSource.Accrued && e.Rule.Predicate == predicate);

        var delta = direction == TasteThumbDirection.Up ? Step : -Step;
        var now = DateTime.UtcNow;
        double weight;
        if (existing is not null)
        {
            weight = Math.Clamp(existing.Rule.Weight + delta, -1.0, 1.0);
            TasteRows.Remove(existing);
            TasteRows.Add(existing with { Rule = existing.Rule with { Weight = weight }, UpdatedAt = now });
        }
        else
        {
            weight = Math.Clamp(delta, -1.0, 1.0);
            TasteRows.Add(new PersonaTasteEntry(
                nextTasteId++, personaId, new TasteRule(predicate, new TasteContext([], null, null), weight),
                PersonaTasteSource.Accrued, now, now));
        }

        // F84.3 — cap-50-weakest-evicted, scoped to source='accrued' only (authored/operator rows
        // are never candidates).
        var accrued = TasteRows
            .Where(e => e.PersonaId == personaId && e.Source == PersonaTasteSource.Accrued)
            .OrderByDescending(e => Math.Abs(e.Rule.Weight))
            .ThenByDescending(e => e.CreatedAt)
            .ToList();
        foreach (var evictable in accrued.Skip(Cap))
            TasteRows.Remove(evictable);

        return Task.FromResult<TasteThumbOutcome>(new TasteThumbOutcome.Nudged(personaId, weight));
    }
}

/// <summary>Deterministic <see cref="IRandomSource"/> double backed by a seeded <see cref="Random"/> — mirrors
/// GenWave.Orchestration.Tests' own <c>SeededRandomSource</c> (a different test assembly, so this file
/// scripts its own minimal copy rather than reaching across projects).</summary>
file sealed class SeededRandomSource(int seed) : IRandomSource
{
    readonly Random random = new(seed);
    public double NextDouble() => random.NextDouble();
}

/// <summary>
/// <see cref="IPersonaTasteReader"/> double backed by an in-memory list that starts empty and that
/// NOTHING in the ranker's own dependency graph can ever add to (the interface has no write member)
/// — the zero-input simulation's "no taste rows were written" claim (F84.2) is exactly "this list is
/// still empty after the run", made against the REAL ranker rather than a mock of it.
/// </summary>
file sealed class EmptyPersonaTasteReader : IPersonaTasteReader
{
    public List<PersonaTasteEntry> Rows { get; } = [];

    public Task<IReadOnlyList<PersonaTasteEntry>> ListAsync(long personaId, PersonaTasteSource? source, CancellationToken ct)
    {
        IReadOnlyList<PersonaTasteEntry> result = Rows
            .Where(r => r.PersonaId == personaId && (source is null || r.Source == source))
            .ToList();
        return Task.FromResult(result);
    }
}

/// <summary>
/// Minimal, call-recording <see cref="IMediaRating"/> double for the F84.7 disjointness facts only —
/// every member this scenario pair does not exercise throws, mirroring the "duplicating the minimal
/// shape is cheaper than threading a fuller fake across files" idiom Story209_PersonaImport.cs's own
/// station-A fakes already use.
/// </summary>
file sealed class RecordingMediaRating : IMediaRating
{
    public VoteOutcome VoteResult { get; set; } = new(RatingWriteResult.Updated, 51);
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
        return Task.FromResult(new NeverPlayOutcome(RatingWriteResult.Updated, neverPlay));
    }

    public Task<IReadOnlyList<MediaRating>> GetRatingsAsync(IReadOnlyList<string> mediaIds, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by the F84.7 disjointness facts.");

    public Task<int> BulkVoteAsync(MediaQuery filter, VoteDirection direction, LibraryScope scope, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by the F84.7 disjointness facts.");

    public Task<int> BulkSetNeverPlayAsync(MediaQuery filter, bool neverPlay, LibraryScope scope, CancellationToken ct) =>
        throw new NotSupportedException("Not exercised by the F84.7 disjointness facts.");
}

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline (routing,
/// auth, the production thumb AND vote routes) while replacing <see cref="IPersonaTasteAccrualStore"/>/
/// <see cref="IMediaRating"/> with recording fakes bound at container scope — mirrors Story209's
/// <c>PersonaImportWebFactory</c>. Both fakes are reachable from EITHER route through the SAME
/// container, so a regression that crossed the two write paths would show up here even though this
/// file never threads one fake directly into the other controller's constructor.
/// </summary>
file sealed class TasteDisjointnessWebFactory(
    FakePersonaTasteAccrualStore? accrual = null,
    RecordingMediaRating? rating = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IPersonaTasteAccrualStore>();
            services.AddSingleton<IPersonaTasteAccrualStore>(accrual ?? new FakePersonaTasteAccrualStore());

            services.RemoveAll<IMediaRating>();
            services.AddSingleton<IMediaRating>(rating ?? new RecordingMediaRating());

            // gh-#99: ThumbTaste now resolves the row's stamped media id (safe-content exclusion)
            // before the accrual store is ever reached — the real BoothLogRepository would lazily
            // build a station data source from this factory's empty connection string and throw.
            // An empty reader answers "no media id" for every row, which is exactly the un-stamped,
            // non-excluded posture these disjointness facts assume.
            services.RemoveAll<IBoothLogReader>();
            services.AddSingleton<IBoothLogReader>(TasteLearningFixture.EmptyReader());
        });
    }
}

/// <summary>
/// File-scoped helpers whose signatures carry file-local types (<see cref="FixedPageBoothLogReader"/>,
/// <see cref="FakePersonaTasteAccrualStore"/>, <see cref="EmptyPersonaTasteReader"/>) — CS9051 forbids
/// those appearing in a member of the public <see cref="FeatureTasteLearningGuardrails"/> class, so
/// (mirrors Story209_PersonaImport.cs's own <c>PersonaReImportFixture</c>/<c>StationARoundTripFixture</c>
/// precedent) they live here instead, in a type that is itself file-scoped.
/// </summary>
file static class TasteLearningFixture
{
    public static FixedPageBoothLogReader EmptyReader() => new(new BoothLogPage([], NextBefore: null));

    public static void SeedAccruedRows(FakePersonaTasteAccrualStore accrual, long personaId, IEnumerable<(string Artist, double Weight)> rows)
    {
        var now = DateTime.UtcNow;
        var id = 1000L;
        foreach (var (artist, weight) in rows)
        {
            accrual.TasteRows.Add(new PersonaTasteEntry(
                id++, personaId, new TasteRule(new TastePredicate(artist, null, null), new TasteContext([], null, null), weight),
                PersonaTasteSource.Accrued, now, now));
        }
    }

    /// <summary>
    /// Runs 500 real <see cref="PersonaRanker.PickAsync"/> picks with ZERO operator input (an
    /// <see cref="EmptyPersonaTasteReader"/> that starts and stays empty) over a pool of 12 distinct,
    /// evenly-weighted artists — <c>TopK</c> is set to the full pool size so every candidate always
    /// enters the softmax sample (no Top-K-truncation tie artifact skewing the distribution for
    /// reasons unrelated to F84.2). Both facts that need this share the one helper; each gets its own
    /// fresh, deterministic (seeded RNG) run.
    /// </summary>
    public static async Task<(EmptyPersonaTasteReader Reader, IReadOnlyDictionary<string, int> PicksByArtist)> RunZeroInputSimulationAsync()
    {
        const int artistCount = 12;
        const int iterations = 500;

        var pool = Enumerable.Range(0, artistCount)
            .Select(i => new PersonaRankCandidate(
                MediaId: $"m{i}", Artist: $"Artist{i}", Genre: "Rock", Moods: [], Energy: 0.5, RotationScore: 1.0))
            .ToList();

        var reader = new EmptyPersonaTasteReader();
        var ranker = new PersonaRanker(
            reader, new SeededRandomSource(seed: 84), TimeProvider.System,
            new PersonaRankerOptions { TopK = artistCount }, NullLogger<PersonaRanker>.Instance);
        var range = new EnergyRange(0.0, 1.0);

        var picks = pool.ToDictionary(c => c.Artist!, _ => 0);
        for (var i = 0; i < iterations; i++)
        {
            var result = await ranker.PickAsync(personaId: 1, energyDisposition: 0.0, range, pool, CancellationToken.None);
            Assert.NotNull(result);
            picks[result.Candidate.Artist!]++;
        }

        return (reader, picks);
    }
}

public static class FeatureTasteLearningGuardrails
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = TasteDisjointnessWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    public static class ScenarioThumbNudgesAnArtistRule
    {
        // Arrange (T70): a track airing under an active persona; POST a thumb via the API.

        [Fact]
        public static async Task AFirstThumbCreatesAnAccruedArtistRule()
        {
            var accrual = new FakePersonaTasteAccrualStore();
            accrual.Rows.Add(new FakeBoothLogRow { Id = 1, PersonaId = 7, Artist = "The Waveforms" });
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            var result = await controller.ThumbTaste(1, new TasteThumbRequest("up"), CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var body = Assert.IsType<TasteThumbResponse>(ok.Value);
            Assert.False(body.AlreadyRecorded);
            Assert.Equal(0.2, body.Weight);

            // predicate = artist, source='accrued', weight ±0.2 (F84.1)
            var rule = Assert.Single(accrual.TasteRows, r => r.PersonaId == 7);
            Assert.Equal(PersonaTasteSource.Accrued, rule.Source);
            Assert.Equal("The Waveforms", rule.Rule.Predicate.Artist);
            Assert.Equal(0.2, rule.Rule.Weight);
        }

        [Fact]
        public static async Task ARepeatThumbNudgesByOneStep()
        {
            // Two DIFFERENT airings of the same artist under the same persona — not a double-tap
            // (F84.5 governs the SAME airing only), so both nudge the rule.
            var accrual = new FakePersonaTasteAccrualStore();
            accrual.Rows.Add(new FakeBoothLogRow { Id = 1, PersonaId = 7, Artist = "The Waveforms" });
            accrual.Rows.Add(new FakeBoothLogRow { Id = 2, PersonaId = 7, Artist = "The Waveforms" });
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            await controller.ThumbTaste(1, new TasteThumbRequest("up"), CancellationToken.None);
            await controller.ThumbTaste(2, new TasteThumbRequest("up"), CancellationToken.None);

            var rule = Assert.Single(accrual.TasteRows, r => r.PersonaId == 7);
            Assert.Equal(0.4, rule.Rule.Weight);
        }

        [Fact]
        public static async Task WeightClampsAtTheBounds()
        {
            // six same-direction thumbs on distinct airings ⇒ weight is 1.0, not 1.2 (F84.1)
            var accrual = new FakePersonaTasteAccrualStore();
            for (var i = 1; i <= 6; i++)
                accrual.Rows.Add(new FakeBoothLogRow { Id = i, PersonaId = 7, Artist = "The Waveforms" });
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            for (var i = 1; i <= 6; i++)
                await controller.ThumbTaste(i, new TasteThumbRequest("up"), CancellationToken.None);

            var rule = Assert.Single(accrual.TasteRows, r => r.PersonaId == 7);
            Assert.Equal(1.0, rule.Rule.Weight);
        }
    }

    public static class ScenarioBoothLogAttribution
    {
        // Arrange (T60/T70): a row aired under persona A; persona B is now active; thumb the row.

        [Fact]
        public static async Task TrackRowsCarryTheOnAirPersonaId()
        {
            // Given a booth-log track row stamped with the persona that was active when it aired
            // (F84.6) — the reader/API path never re-derives this from whatever persona happens to
            // be active NOW; it simply reflects whatever the write path stamped.
            const long rowId = 1;
            const long personaAId = 7;
            var stampedRow = new BoothLogEntry(rowId, DateTime.UtcNow, "track-started",
                "Started 'Night Drive' by The Waveforms", PersonaId: personaAId);
            var reader = new FixedPageBoothLogReader(new BoothLogPage([stampedRow], NextBefore: null));
            var controller = new BoothLogController(reader, new FakePersonaTasteAccrualStore(), new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            // When the admin feed reads it back...
            var page = Assert.IsType<BoothLogPageDto>(
                Assert.IsType<OkObjectResult>(await controller.List(before: null, take: 10, CancellationToken.None)).Value);

            // Then the API row carries that same persona id — stamped at air time, additive
            // column/payload (F84.6) — AND the row's own DB id (F84.1, PLAN T71): the admin UI's
            // taste-thumb POST target (POST /api/booth-log/{id}/taste-thumb) is this exact value,
            // never re-derived, so the projection must not drop it.
            var entry = page.Entries.Single();
            Assert.Equal(personaAId, entry.PersonaId);
            Assert.Equal(rowId, entry.Id);
        }

        [Fact]
        public static async Task TheRuleAccruesToThePersonaOnAirAtTheAiring()
        {
            // Given a booth-log row stamped with persona A at air time, while persona B (the
            // notionally "now active" persona) already holds its OWN accrued rule for the exact same
            // artist — the fact this arrangement must disprove is "the rule accrues to whoever is
            // active now", so B starts with a rule that would move if that were true.
            const long personaAId = 5;
            const long personaBId = 9;
            var accrual = new FakePersonaTasteAccrualStore();
            accrual.Rows.Add(new FakeBoothLogRow { Id = 1, PersonaId = personaAId, Artist = "The Waveforms" });
            accrual.TasteRows.Add(new PersonaTasteEntry(
                1, personaBId, new TasteRule(new TastePredicate("The Waveforms", null, null), new TasteContext([], null, null), 0.6),
                PersonaTasteSource.Accrued, DateTime.UtcNow, DateTime.UtcNow));
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            // When the operator thumbs that row up...
            await controller.ThumbTaste(1, new TasteThumbRequest("up"), CancellationToken.None);

            // Then the rule accrues to A (persona A, never the now-active B, F84.1) — A's rule is
            // freshly created at the first step (0.2); B's own pre-existing rule is untouched.
            var ruleA = Assert.Single(accrual.TasteRows, r => r.PersonaId == personaAId);
            Assert.Equal(0.2, ruleA.Rule.Weight);

            var ruleB = Assert.Single(accrual.TasteRows, r => r.PersonaId == personaBId);
            Assert.Equal(0.6, ruleB.Rule.Weight);
        }
    }

    public static class ScenarioCapAndEviction
    {
        // Arrange (T70): a persona at the 50 accrued-rule cap plus authored/operator rows.

        [Fact]
        public static async Task TheWeakestAccruedRuleIsEvictedInTheSameTransaction()
        {
            const long personaId = 4;
            var accrual = new FakePersonaTasteAccrualStore();
            // 50 accrued rows already at the cap — Artist0 is deliberately the weakest.
            var seeded = Enumerable.Range(0, 50).Select(i => (Artist: $"Artist{i}", Weight: i == 0 ? 0.01 : 0.5));
            TasteLearningFixture.SeedAccruedRows(accrual, personaId, seeded);
            accrual.Rows.Add(new FakeBoothLogRow { Id = 1, PersonaId = personaId, Artist = "New Artist" });
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            // When a thumb creates a new (51st) rule...
            await controller.ThumbTaste(1, new TasteThumbRequest("up"), CancellationToken.None);

            // Then the lowest-|weight| accrued rule is evicted; row count stays 50 (F84.3).
            var accrued = accrual.TasteRows.Where(r => r.PersonaId == personaId && r.Source == PersonaTasteSource.Accrued).ToList();
            Assert.Equal(50, accrued.Count);
            Assert.DoesNotContain(accrued, r => r.Rule.Predicate.Artist == "Artist0");
            Assert.Contains(accrued, r => r.Rule.Predicate.Artist == "New Artist");
        }

        [Fact]
        public static async Task AuthoredAndOperatorRulesAreNeverEvicted()
        {
            const long personaId = 4;
            var accrual = new FakePersonaTasteAccrualStore();
            TasteLearningFixture.SeedAccruedRows(accrual, personaId, Enumerable.Range(0, 50).Select(i => (Artist: $"Artist{i}", Weight: 0.5)));

            var now = DateTime.UtcNow;
            var authored = new PersonaTasteEntry(
                9001, personaId, new TasteRule(new TastePredicate("Led Zeppelin", null, null), new TasteContext([], null, null), 0.9),
                PersonaTasteSource.Authored, now, now);
            var operatorRow = new PersonaTasteEntry(
                9002, personaId, new TasteRule(new TastePredicate(null, "Vaporwave", null), new TasteContext([], null, null), -0.9),
                PersonaTasteSource.Operator, now, now);
            accrual.TasteRows.Add(authored);
            accrual.TasteRows.Add(operatorRow);
            accrual.Rows.Add(new FakeBoothLogRow { Id = 1, PersonaId = personaId, Artist = "New Artist" });
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            // When a thumb pushes the accrued count past the cap...
            await controller.ThumbTaste(1, new TasteThumbRequest("up"), CancellationToken.None);

            // Then the card's signature (authored) and the direct operator edit both survive
            // untouched — the card's signature cannot be crowded out (F84.3).
            Assert.Contains(authored, accrual.TasteRows);
            Assert.Contains(operatorRow, accrual.TasteRows);
            Assert.Equal(50, accrual.TasteRows.Count(r => r.PersonaId == personaId && r.Source == PersonaTasteSource.Accrued));
        }
    }

    public static class SadPathStructuralGuardrails
    {
        [Fact]
        public static async Task FiveHundredPicksWithZeroInputWriteNoTasteRows()
        {
            // the ranker/feeder have no persona_taste write path (F84.2 structural) — proven here by
            // running the REAL ranker with zero operator input and observing its own backing store
            // never gained a row (it structurally has no write member to gain one through).
            var (reader, _) = await TasteLearningFixture.RunZeroInputSimulationAsync();

            Assert.Empty(reader.Rows);
        }

        [Fact]
        public static async Task FiveHundredPicksKeepEveryArtistWithinRotationShare()
        {
            // no self-reinforcement spiral (F84.2 simulation). 3x the uniform 1/12 (~8.3%) baseline is
            // a generous bound that still trips on a genuine runaway artist: binomial(500, 1/12) puts
            // 125 picks over 13 standard deviations from the expected ~41.7 mean, so this is nowhere
            // near RNG noise territory.
            const double maxShare = 0.25;
            var (_, picks) = await TasteLearningFixture.RunZeroInputSimulationAsync();

            foreach (var (artist, count) in picks)
            {
                Assert.True(count <= 500 * maxShare,
                    $"{artist} was picked {count}/500 times — exceeds the rotation-policy share bound");
            }
        }

        [Fact]
        public static async Task ADoubleTapMovesTheWeightOnce()
        {
            // idempotent per (persona, airing, direction) (F84.5)
            var accrual = new FakePersonaTasteAccrualStore();
            accrual.Rows.Add(new FakeBoothLogRow { Id = 1, PersonaId = 7, Artist = "The Waveforms" });
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            await controller.ThumbTaste(1, new TasteThumbRequest("up"), CancellationToken.None);
            var second = await controller.ThumbTaste(1, new TasteThumbRequest("up"), CancellationToken.None);

            var body = Assert.IsType<TasteThumbResponse>(Assert.IsType<OkObjectResult>(second).Value);
            Assert.True(body.AlreadyRecorded);

            var rule = Assert.Single(accrual.TasteRows, r => r.PersonaId == 7);
            Assert.Equal(0.2, rule.Rule.Weight);
        }

        [Fact]
        public static async Task NowPlayingAndBoothLogTapsOnTheSameAiringMoveTheWeightOnce()
        {
            // T70's design: the now-playing surface resolves to its own latest track-start booth-log
            // row and posts to the exact same route this fact drives directly — there is no second
            // endpoint shape for a now-playing tap to diverge from, so "a now-playing tap and a
            // booth-log tap on the same airing" and "two taps on the same route" are the identical
            // call from the server's point of view (F84.5).
            var accrual = new FakePersonaTasteAccrualStore();
            accrual.Rows.Add(new FakeBoothLogRow { Id = 9, PersonaId = 3, Artist = "Static" });
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            await controller.ThumbTaste(9, new TasteThumbRequest("down"), CancellationToken.None);
            var second = await controller.ThumbTaste(9, new TasteThumbRequest("down"), CancellationToken.None);

            var body = Assert.IsType<TasteThumbResponse>(Assert.IsType<OkObjectResult>(second).Value);
            Assert.True(body.AlreadyRecorded);

            var rule = Assert.Single(accrual.TasteRows, r => r.PersonaId == 3);
            Assert.Equal(-0.2, rule.Rule.Weight);
        }

        [Fact]
        public static async Task AnUnstampedRowRejectsAThumb()
        {
            // rows predating the stamp (or persona-less airings) are not thumbable for taste (F84.6)
            var accrual = new FakePersonaTasteAccrualStore();
            accrual.Rows.Add(new FakeBoothLogRow { Id = 2, PersonaId = null, Artist = null });
            var controller = new BoothLogController(TasteLearningFixture.EmptyReader(), accrual, new FakeMediaLibraryMembership(), new FakeSafeScopeProvider(), NullLogger<BoothLogController>.Instance);

            var result = await controller.ThumbTaste(2, new TasteThumbRequest("up"), CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.IsType<ProblemDetails>(badRequest.Value);
            Assert.Empty(accrual.TasteRows);
        }

        [Fact]
        public static async Task APersonaThumbNeverWritesMediaRating()
        {
            // F84.7 disjointness, half one — driven through the real HTTP pipeline so the OTHER
            // store, bound in the SAME container, would catch a regression this file's own fakes-only
            // wiring could not.
            var accrual = new FakePersonaTasteAccrualStore();
            accrual.Rows.Add(new FakeBoothLogRow { Id = 1, PersonaId = 7, Artist = "The Waveforms" });
            var rating = new RecordingMediaRating();

            await using var factory = new TasteDisjointnessWebFactory(accrual, rating);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/booth-log/1/taste-thumb", new { direction = "up" });

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Empty(rating.VoteCalls);
            Assert.Empty(rating.NeverPlayCalls);
        }

        [Fact]
        public static async Task AnF33VoteNeverWritesPersonaTaste()
        {
            // F84.7 disjointness, half two
            var accrual = new FakePersonaTasteAccrualStore();
            var rating = new RecordingMediaRating();

            await using var factory = new TasteDisjointnessWebFactory(accrual, rating);
            var client = await LoggedInClientAsync(factory);

            var response = await client.PostAsJsonAsync("/api/media/42/vote", new { direction = "up" });

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Empty(accrual.ThumbCalls);
            Assert.Empty(accrual.TasteRows);
        }
    }
}
