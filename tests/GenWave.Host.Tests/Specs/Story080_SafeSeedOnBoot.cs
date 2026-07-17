// STORY-080 — Fresh deploys seed a branded backstop (WIRE)
//
// BDD specification — xUnit. SPEC F27.6 / F27.10 / F21.8 (amendment). One-shot
// idempotent IHostedService: marker key in station.settings (never returned by
// GET /api/settings); fresh boot creates library "safe", renders a voice-only
// segment from Station:Safe:SeedMessage ({StationName} expanded, voice
// Station:Voice), and — iff no operator SafeScope value exists — writes the
// overlay [safe-library-id]. Failure = WARN + normal boot + retry next boot.
// NEVER touches Station:Scope:LibraryIds; NEVER overwrites an operator
// SafeScope. P7 wires SafeLoopSeeder/SafeLoopSeedHostedService (Host/Seeding);
// live proofs against the real stack are P9(a), operator-gated.
//
// In-process tests (FeatureSafeSeedOnBootInProcess): construct SafeLoopSeeder
// directly with fakes (ISafeLoopSeedMarkerStore, ILibraryRepository,
// IAdminLibraryWrite, ISafeSegmentAuthor, IStationSettingsStore) — no live
// stack required. Mirrors the Story079 pattern. One scenario also drives
// SafeLoopSeedHostedService directly to prove the fire-and-forget shape never
// blocks host startup.
//
// Operator-gated integration scenarios (FeatureSafeSeedOnBoot): remain
// Skip-pinned — a real Postgres row, a real Kokoro render, and a real file
// under /authored all need the live stack (P9(a)).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Host.Seeding;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> that always returns the given value.</summary>
file sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>In-memory <see cref="ISafeLoopSeedMarkerStore"/>.</summary>
file sealed class FakeMarkerStore : ISafeLoopSeedMarkerStore
{
    public bool Present { get; set; }
    public int MarkCompletedCallCount { get; private set; }

    public Task<bool> ExistsAsync(CancellationToken ct) => Task.FromResult(Present);

    public Task MarkCompletedAsync(CancellationToken ct)
    {
        MarkCompletedCallCount++;
        Present = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory <see cref="IStationSettingsStore"/>. Records every <see cref="WriteAsync"/> call so
/// tests can assert exactly which keys the seeder touched — in particular that
/// <c>Station:Scope:LibraryIds</c> never appears (STORY-080 AC3).
/// </summary>
file sealed class FakeSeedSettingsStore : IStationSettingsStore
{
    readonly Dictionary<string, string> rows = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Key, object Value)> WriteCalls { get; } = [];

    /// <summary>Pre-seeds a row as if a prior operator PUT /api/settings had already run.</summary>
    public void SeedOperatorRow(string key, string rawJsonValue) => rows[key] = rawJsonValue;

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        WriteCalls.Add((key, value));
        rows[key] = System.Text.Json.JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, string> snapshot =
            new Dictionary<string, string>(rows, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(snapshot);
    }
}

/// <summary>
/// In-memory library catalog implementing both <see cref="ILibraryRepository"/> (read) and
/// <see cref="IAdminLibraryWrite"/> (create) — the two seams <see cref="SafeLoopSeeder"/> uses to
/// "create if absent" (F27.6 step a). Production splits these across two repositories; one fake
/// covering both is simpler here and behaviourally equivalent for these tests.
/// </summary>
file sealed class FakeLibraryStore : ILibraryRepository, IAdminLibraryWrite
{
    readonly List<LibraryAdminInfo> libraries = [];
    long nextId = 1;

    public int CreateCallCount { get; private set; }

    /// <summary>
    /// Pre-seeds an existing library (simulates one the seed should find and reuse). A non-zero
    /// <paramref name="mediaCount"/> simulates a library that already holds the rendered row (e.g. a
    /// prior attempt that failed after rendering but before the overlay/marker step) — the seeder must
    /// skip re-rendering in that case (F27.6).
    /// </summary>
    public long AddExisting(string name, int mediaCount = 0)
    {
        var id = nextId++;
        libraries.Add(new LibraryAdminInfo(id, name, mediaCount));
        return id;
    }

    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryInfo>>(
            libraries.Where(l => ids.Contains(l.Id)).Select(l => new LibraryInfo(l.Id, l.Name)).ToList());

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LibraryAdminInfo>>(libraries.ToList());

    public Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct)
    {
        CreateCallCount++;
        if (libraries.Any(l => string.Equals(l.Name, name, StringComparison.Ordinal)))
            return Task.FromResult<LibraryWriteResult>(new LibraryWriteResult.NameConflict());

        var id = nextId++;
        libraries.Add(new LibraryAdminInfo(id, name, 0));
        return Task.FromResult<LibraryWriteResult>(new LibraryWriteResult.Created(id));
    }

    public Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the boot seed.");

    public Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the boot seed.");
}

/// <summary>
/// <see cref="ISafeLoopSeedMarkerStore"/> double whose <see cref="ExistsAsync"/> always throws —
/// simulates the most common boot-time transient (the station DB not yet reachable) so tests can
/// prove it degrades to <see cref="SafeLoopSeedOutcome.Failed"/> instead of escaping <c>SeedAsync</c>
/// and landing in the hosted service's last-resort catch (F27.6 AC4).
/// </summary>
file sealed class ThrowingMarkerStore : ISafeLoopSeedMarkerStore
{
    public Task<bool> ExistsAsync(CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task MarkCompletedAsync(CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");
}

/// <summary>
/// <see cref="ILibraryRepository"/>/<see cref="IAdminLibraryWrite"/> double that always throws —
/// simulates an unexpected DB fault so tests can prove <see cref="SafeLoopSeeder.SeedAsync"/> degrades
/// instead of propagating (F27.6 AC4: never blocks boot).
/// </summary>
file sealed class ThrowingLibraryRepository : ILibraryRepository, IAdminLibraryWrite
{
    public Task<IReadOnlyList<LibraryInfo>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<IReadOnlyList<LibraryAdminInfo>> GetAllWithMediaCountAsync(CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<LibraryWriteResult> CreateAsync(string name, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<LibraryWriteResult> RenameAsync(long id, string name, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the boot seed.");

    public Task<LibraryWriteResult> DeleteAsync(long id, CancellationToken ct) =>
        throw new NotSupportedException("Not used by the boot seed.");
}

/// <summary>
/// Scriptable <see cref="ISafeSegmentAuthor"/>. Set <see cref="Result"/> for an immediate outcome, or
/// <see cref="Gate"/> to suspend until the test releases it — used to prove the hosted service's
/// <c>StartAsync</c> returns while the render is still in flight (F27.6: "must not block host
/// startup").
/// </summary>
file sealed class FakeSeedSafeSegmentAuthor : ISafeSegmentAuthor
{
    public SafeSegmentAuthorResult? Result { get; set; }
    public TaskCompletionSource<SafeSegmentAuthorResult>? Gate { get; set; }
    public SafeSegmentRequest? LastRequest { get; private set; }
    public int CallCount { get; private set; }

    public async Task<SafeSegmentAuthorResult> AuthorAsync(SafeSegmentRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;

        if (Gate is not null)
            return await Gate.Task;

        return Result ?? throw new InvalidOperationException("Result not set");
    }
}

/// <summary>Builds a <see cref="SafeLoopSeeder"/> wired to fresh fakes.</summary>
file static class SafeLoopSeederFactory
{
    public static (
        SafeLoopSeeder Seeder,
        FakeMarkerStore Marker,
        FakeLibraryStore Libraries,
        FakeSeedSafeSegmentAuthor Author,
        FakeSeedSettingsStore Settings) Build(StationOptions? stationOptions = null)
    {
        var marker = new FakeMarkerStore();
        var libraries = new FakeLibraryStore();
        var author = new FakeSeedSafeSegmentAuthor();
        var settings = new FakeSeedSettingsStore();

        var seeder = new SafeLoopSeeder(
            marker,
            libraries,
            libraries,
            author,
            settings,
            new FakeOptionsMonitor<StationOptions>(stationOptions ?? DefaultStationOptions()),
            NullLogger<SafeLoopSeeder>.Instance);

        return (seeder, marker, libraries, author, settings);
    }

    public static StationOptions DefaultStationOptions() => new()
    {
        Id    = "test",
        Name  = "Test Station",
        Voice = "af_heart",
        Safe  = new StationSafeOptions
        {
            SeedMessage   = "You're listening to {StationName}. Stand by.",
            AuthoredRoot  = "/authored",
            BedDuckDb     = -12.0,
            BedPadSeconds = 1.5,
        },
    };
}

// ── In-process tests ──────────────────────────────────────────────────────────────────────────────

public static class FeatureSafeSeedOnBootInProcess
{
    // ── AC1 — first boot seeds library, row, and scope ──────────────────────────────────────────

    public sealed class ScenarioFirstBootSeedsLibraryRowAndScope
    {
        [Fact]
        public async Task NoExistingSafeLibraryIsCreated()
        {
            var (seeder, _, libraries, author, _) = SafeLoopSeederFactory.Build();
            author.Result = SafeSegmentAuthorResult.Success(42);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(SafeLoopSeedOutcome.Seeded, outcome);
            Assert.Equal(1, libraries.CreateCallCount);
            var all = await libraries.GetAllWithMediaCountAsync(CancellationToken.None);
            Assert.Contains(all, l => l.Name == SafeLoopSeeder.SafeLibraryName);
        }

        [Fact]
        public async Task AnExistingSafeLibraryIsReusedNotRecreated()
        {
            var (seeder, _, libraries, author, _) = SafeLoopSeederFactory.Build();
            var existingId = libraries.AddExisting(SafeLoopSeeder.SafeLibraryName);
            author.Result = SafeSegmentAuthorResult.Success(42);

            await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(0, libraries.CreateCallCount);
            Assert.NotNull(author.LastRequest);
            Assert.Equal(existingId, author.LastRequest.LibraryId);
        }

        [Fact]
        public async Task TheRenderRequestPassesTheRawTemplateAndCarriesResolvedStationValues()
        {
            // R1 (SPEC F29.1-F29.3, STORY-095/096): {StationName} expansion moved into
            // SafeSegmentAuthor.AuthorAsync — the seeder no longer pre-expands, so the request it
            // hands to the author still carries the literal token. See Story095's
            // ScenarioExpansionLivesAtTheAuthor for the author-side expansion proof, and this
            // scenario's own end-to-end fact for proof the seeded segment still speaks the real name.
            var stationOptions = SafeLoopSeederFactory.DefaultStationOptions();
            var (seeder, _, _, author, _) = SafeLoopSeederFactory.Build(stationOptions);
            author.Result = SafeSegmentAuthorResult.Success(42);

            await seeder.SeedAsync(CancellationToken.None);

            Assert.NotNull(author.LastRequest);
            Assert.Equal("You're listening to {StationName}. Stand by.", author.LastRequest.Text);
            Assert.Equal("Test Station", author.LastRequest.StationName);
            Assert.Equal("af_heart", author.LastRequest.DefaultVoice);
            Assert.Equal("/authored", author.LastRequest.AuthoredRoot);
            Assert.Equal(-12.0, author.LastRequest.BedDuckDb);
            Assert.Equal(1.5, author.LastRequest.BedPadSeconds);
            // Voice left null so SafeSegmentAuthor applies its own default (Station:Voice) —
            // identical to how P6's endpoint defers to the author (F27.3). Title is now the
            // explicit SafeLoopSeeder.SeedTitle (STORY-096), asserted by Story096's own fact.
            Assert.Equal(SafeLoopSeeder.SeedTitle, author.LastRequest.Title);
            Assert.Null(author.LastRequest.Voice);
            Assert.Null(author.LastRequest.Bed);
        }

        [Fact]
        public async Task TheSafeScopeOverlayIsWrittenWithTheSeededLibraryId()
        {
            var (seeder, _, libraries, author, settings) = SafeLoopSeederFactory.Build();
            author.Result = SafeSegmentAuthorResult.Success(42);

            await seeder.SeedAsync(CancellationToken.None);

            var write = Assert.Single(settings.WriteCalls);
            Assert.Equal("Station:SafeScope:LibraryIds", write.Key);
            var libraryId = (await libraries.GetAllWithMediaCountAsync(CancellationToken.None)).Single().Id;
            var storedIds = Assert.IsType<long[]>(write.Value);
            Assert.Equal([libraryId], storedIds);
        }

        [Fact]
        public async Task TheMarkerIsWrittenOnSuccess()
        {
            var (seeder, marker, _, author, _) = SafeLoopSeederFactory.Build();
            author.Result = SafeSegmentAuthorResult.Success(42);

            await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(1, marker.MarkCompletedCallCount);
        }
    }

    // ── AC2 — second boot is a no-op ─────────────────────────────────────────────────────────────

    public sealed class ScenarioSecondBootIsANoOp
    {
        [Fact]
        public async Task ABootWithTheMarkerPresentSeedsNothing()
        {
            var (seeder, marker, libraries, author, settings) = SafeLoopSeederFactory.Build();
            marker.Present = true;

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(SafeLoopSeedOutcome.AlreadySeeded, outcome);
            Assert.Equal(0, libraries.CreateCallCount);
            Assert.Equal(0, author.CallCount);
            Assert.Empty(settings.WriteCalls);
            Assert.Equal(0, marker.MarkCompletedCallCount);
        }
    }

    // ── Retry-after-partial-failure — a library that already holds the rendered row is reused ────

    public sealed class ScenarioExistingContentIsReusedNotDuplicated
    {
        [Fact]
        public async Task AnExistingSafeLibraryWithRowsSkipsRenderButStillWritesOverlayAndMarker()
        {
            // Simulates retrying after a prior attempt rendered the row but failed before the
            // overlay/marker step (F27.6): the library already has content, so this attempt must not
            // render a second "Please Stand By" row — it should just finish the remaining steps.
            var (seeder, marker, libraries, author, settings) = SafeLoopSeederFactory.Build();
            var existingId = libraries.AddExisting(SafeLoopSeeder.SafeLibraryName, mediaCount: 1);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(SafeLoopSeedOutcome.Seeded, outcome);
            Assert.Equal(0, author.CallCount);
            Assert.Equal(0, libraries.CreateCallCount);
            var write = Assert.Single(settings.WriteCalls);
            Assert.Equal("Station:SafeScope:LibraryIds", write.Key);
            var storedIds = Assert.IsType<long[]>(write.Value);
            Assert.Equal([existingId], storedIds);
            Assert.Equal(1, marker.MarkCompletedCallCount);
        }
    }

    // ── AC3 — operator SafeScope is never clobbered; Scope is never touched ─────────────────────

    public sealed class ScenarioOperatorSafeScopeIsNeverClobbered
    {
        [Fact]
        public async Task AnExistingOperatorSafeScopeValueIsUntouched()
        {
            // An operator-emptied SafeScope ("[]") still counts as "exists" — a deliberate F25
            // choice — and must be left exactly as-is; only the overlay write is skipped, not the
            // rest of the pipeline (library + row are still seeded, marker still written).
            var (seeder, marker, libraries, author, settings) = SafeLoopSeederFactory.Build();
            settings.SeedOperatorRow("Station:SafeScope:LibraryIds", "[]");
            author.Result = SafeSegmentAuthorResult.Success(42);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(SafeLoopSeedOutcome.Seeded, outcome);
            Assert.Equal(1, libraries.CreateCallCount);
            Assert.Equal(1, author.CallCount);
            Assert.DoesNotContain(settings.WriteCalls, w => w.Key == "Station:SafeScope:LibraryIds");
            Assert.Equal(1, marker.MarkCompletedCallCount);
        }

        [Fact]
        public async Task MainScopeIsNeverReadOrWritten()
        {
            var (seeder, _, _, author, settings) = SafeLoopSeederFactory.Build();
            settings.SeedOperatorRow("Station:Scope:LibraryIds", "[1]");
            author.Result = SafeSegmentAuthorResult.Success(42);

            await seeder.SeedAsync(CancellationToken.None);

            Assert.DoesNotContain(settings.WriteCalls, w => w.Key == "Station:Scope:LibraryIds");
            var stored = await settings.ReadAllAsync(CancellationToken.None);
            Assert.Equal("[1]", stored["Station:Scope:LibraryIds"]);
        }
    }

    // ── AC4 — seed failure degrades, never blocks boot ──────────────────────────────────────────

    public sealed class ScenarioSeedFailureDegradesNeverBlocksBoot
    {
        [Fact]
        public async Task ASynthesisFailureReturnsFailedWithoutThrowingOrWritingAnything()
        {
            var (seeder, marker, _, author, settings) = SafeLoopSeederFactory.Build();
            author.Result = SafeSegmentAuthorResult.Failure(
                SafeSegmentFailureReason.SynthesisFailed, "Kokoro unreachable");

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(SafeLoopSeedOutcome.Failed, outcome);
            Assert.Equal(0, marker.MarkCompletedCallCount);
            Assert.Empty(settings.WriteCalls);
        }

        [Fact]
        public async Task AnUnexpectedExceptionDuringLibraryLookupDegradesInsteadOfThrowing()
        {
            var marker = new FakeMarkerStore();
            var throwingLibraries = new ThrowingLibraryRepository();
            var author = new FakeSeedSafeSegmentAuthor();
            var settings = new FakeSeedSettingsStore();
            var seeder = new SafeLoopSeeder(
                marker, throwingLibraries, throwingLibraries, author, settings,
                new FakeOptionsMonitor<StationOptions>(SafeLoopSeederFactory.DefaultStationOptions()),
                NullLogger<SafeLoopSeeder>.Instance);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(SafeLoopSeedOutcome.Failed, outcome);
            Assert.Equal(0, marker.MarkCompletedCallCount);
            Assert.Equal(0, author.CallCount);
        }

        [Fact]
        public async Task AThrowingMarkerStoreDegradesInsteadOfThrowing()
        {
            // The marker check is the most common boot-time transient (DB not yet reachable) — it
            // must be handled by SeedAsync itself, not escape to the hosted service's last-resort
            // catch (review follow-up on the initial P7 cut).
            var throwingMarker = new ThrowingMarkerStore();
            var libraries = new FakeLibraryStore();
            var author = new FakeSeedSafeSegmentAuthor();
            var settings = new FakeSeedSettingsStore();
            var seeder = new SafeLoopSeeder(
                throwingMarker, libraries, libraries, author, settings,
                new FakeOptionsMonitor<StationOptions>(SafeLoopSeederFactory.DefaultStationOptions()),
                NullLogger<SafeLoopSeeder>.Instance);

            var outcome = await seeder.SeedAsync(CancellationToken.None);

            Assert.Equal(SafeLoopSeedOutcome.Failed, outcome);
            Assert.Equal(0, libraries.CreateCallCount);
            Assert.Equal(0, author.CallCount);
        }
    }

    // ── AC5 — marker invisible to the settings API ──────────────────────────────────────────────

    public sealed class ScenarioMarkerIsInvisibleToTheSettingsApi
    {
        [Fact]
        public void TheMarkerKeyIsNotOnTheAllowlist()
        {
            Assert.False(StationSettingsAllowlist.ByKey.ContainsKey(SafeLoopSeedMarkerStore.Key));
        }

        [Fact]
        public async Task GetSettingsNeverReturnsTheMarkerKeyEvenIfTheStoreHoldsARowForIt()
        {
            // Defensive/structural proof: SettingsController.Get iterates StationSettingsAllowlist.All
            // only, so even a hypothetical row for the marker key can never surface (F27.10).
            var config = new ConfigurationBuilder().Build();
            var store = new FakeSeedSettingsStore();
            store.SeedOperatorRow(SafeLoopSeedMarkerStore.Key, "\"2026-01-01T00:00:00Z\"");
            var controller = new SettingsController(
                config, store, new SettingValidator(config), NullLogger<SettingsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            var result = await controller.Get(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var items = Assert.IsAssignableFrom<IEnumerable<SettingDto>>(ok.Value);
            Assert.DoesNotContain(items, i => i.Key == SafeLoopSeedMarkerStore.Key);
        }
    }

    // ── Hosted-service wiring — proves /health can come up while the render is in flight ────────

    public sealed class ScenarioHostedServiceDoesNotBlockStartup
    {
        [Fact]
        public async Task StartAsyncDoesNotWaitForAStillOpenRender()
        {
            var marker = new FakeMarkerStore();
            var libraries = new FakeLibraryStore();
            var author = new FakeSeedSafeSegmentAuthor
            {
                Gate = new TaskCompletionSource<SafeSegmentAuthorResult>(),
            };
            var settings = new FakeSeedSettingsStore();
            var seeder = new SafeLoopSeeder(
                marker, libraries, libraries, author, settings,
                new FakeOptionsMonitor<StationOptions>(SafeLoopSeederFactory.DefaultStationOptions()),
                NullLogger<SafeLoopSeeder>.Instance);
            using var hostedService = new SafeLoopSeedHostedService(
                seeder, NullLogger<SafeLoopSeedHostedService>.Instance);

            var startTask = hostedService.StartAsync(CancellationToken.None);

            // author.Gate is deliberately never completed here — if StartAsync waited for the
            // render, this would hang until the test framework's own timeout. Racing it against a
            // short delay proves it returns long before the render finishes, so /health can come up
            // while the seed keeps running in the background (F27.6).
            var startedQuickly = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(2))) == startTask;
            Assert.True(startedQuickly, "StartAsync must not block on the in-flight render.");
            Assert.Equal(0, marker.MarkCompletedCallCount);   // the pipeline cannot have finished yet

            // Release the gate and let the seed run to completion in the background.
            author.Gate.SetResult(SafeSegmentAuthorResult.Success(42));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (marker.MarkCompletedCallCount == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.Equal(1, author.CallCount);
            Assert.Equal(1, marker.MarkCompletedCallCount);
        }
    }
}

// ── Operator-gated (live stack) ───────────────────────────────────────────────────────────────────

public static class FeatureSafeSeedOnBoot
{
    const string OperatorGated =
        "Operator-gated live proof (F27.6 boot seed — real Postgres/Kokoro/file); see docs/PLAN.md Epic P (P9)";

    // ---------------------------------------------------------------------
    // HAPPY PATH — first boot seeds library, row, and scope
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioFirstBootSeedsLibraryRowAndScope
    {
        [Fact(Skip = OperatorGated)]
        public void ALibraryNamedSafeExistsAfterFirstBoot()
        {
            // AC1 — created if absent (F27.6 step a)
            Assert.Fail("pending live verification");
        }

        [Fact(Skip = OperatorGated)]
        public void TheSafeLibraryHoldsOneReadyAuthoredRow()
        {
            // AC1 — artist = Station:Name, title = "Please Stand By",
            //       rendered from SeedMessage with {StationName} expanded
            Assert.Fail("pending live verification");
        }

        [Fact(Skip = OperatorGated)]
        public void TheSafeScopeOverlayPointsAtTheSafeLibrary()
        {
            // AC1 — station.settings overlay = [safe-library-id] (F27.6 step c)
            Assert.Fail("pending live verification");
        }

        [Fact(Skip = OperatorGated)]
        public void TheMarkerIsWrittenOnSuccess()
        {
            // AC1 — marker present after a successful seed
            Assert.Fail("pending live verification");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — idempotency and operator protection
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSecondBootIsANoOp
    {
        [Fact(Skip = OperatorGated)]
        public void ABootWithTheMarkerPresentSeedsNothing()
        {
            // AC2 — no library, row, overlay, or file created
            Assert.Fail("pending live verification");
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioOperatorSafeScopeIsNeverClobbered
    {
        [Fact(Skip = OperatorGated)]
        public void AnExistingOperatorSafeScopeValueIsUntouched()
        {
            // AC3 — seed creates library+row but skips the overlay write entirely
            Assert.Fail("pending live verification");
        }

        [Fact(Skip = OperatorGated)]
        public void MainScopeIsNeverTouched()
        {
            // AC3 — Station:Scope:LibraryIds has no store row before or after the seed
            Assert.Fail("pending live verification");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — degrade, retry, marker invisibility
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSeedFailureDegradesNeverBlocksBoot
    {
        [Fact(Skip = OperatorGated)]
        public void KokoroUnreachableStillBootsTheHost()
        {
            // AC4 — host starts normally; a WARN names the seed failure
            Assert.Fail("pending live verification");
        }

        [Fact(Skip = OperatorGated)]
        public void NoMarkerIsWrittenOnFailure()
        {
            // AC4 — the seed retries on the next boot
            Assert.Fail("pending live verification");
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioMarkerIsInvisibleToTheSettingsApi
    {
        [Fact(Skip = OperatorGated)]
        public void GetSettingsNeverReturnsTheMarkerKey()
        {
            Assert.Fail("pending live verification");
        }
    }
}
