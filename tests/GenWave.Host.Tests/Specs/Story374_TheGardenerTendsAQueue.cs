// STORY-374 — The gardener tends a self-healing queue (SPEC F153.1–F153.2, F153.9–F153.10 · PLAN T372/T377)
//
// BDD specification — xUnit. AC1/AC2/AC3/AC5/AC6 WIRED at T372; AC4/AC7/AC8/AC10 stay pending T377.
// Entry-point discipline: every fact drives the REAL production binary (WebApplicationFactory<Program>,
// the Story345/Story366/Story367 factory idiom over the ephemeral Postgres — Support/EphemeralStationDatabase).
// AC1/AC2/AC3 resolve the real, container-composed IEnumerable<IGardenerPass> (RemoveAll<IHostedService>
// so GardenerService's own timer never runs) and call the dead_file pass's RunAsync directly — the
// SAME "directly-testable seam, hosted loop removed" posture MediaRotationDrainService.ProcessAsync
// already established one seam over (Story367_TheStationRemembersEveryAiring.cs); IGardenerPass is a
// PUBLIC GenWave.Core.Abstractions port, so this needs no InternalsVisibleTo into GenWave.MediaLibrary
// even though GardenerService/DeadFileGardenerPass themselves stay internal there. AC5/AC6 are the two
// exhibits that genuinely need the SERVICE running unattended: every OTHER hosted service is removed,
// but the real GardenerService registration is captured BY NAME (Story297_ContextTickerWire.cs's own
// "capture the descriptor, RemoveAll, re-add" idiom, one step further — GardenerService's own type
// cannot be named here at all) and re-added, so it ticks for real against a live PeriodicTimer.
// Gardener:IntervalMinutes and Library:ScanIntervalSeconds (GardenerService's own honest first-tick
// test seam — never a direct pass call) are both overridden small so these facts stay fast without
// ever pretending the loop away.

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheGardenerTendsAQueue
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — open, resolve, re-open, dismiss, list, count
    // ---------------------------------------------------------------------

    [Collection(DeadFileLifecycleCollection.Name)]
    public sealed class ScenarioAPassOpensAFinding(DeadFileLifecycleArc arc)
    {
        // Given a row whose predicate for kind K holds and no finding, When the gardener pass for K runs.
        [Fact]
        public void TheFindingIsOpenForThatRowAndKind()
        {
            Assert.Equal("open", arc.StateAfterOpen);
        }

        [Fact]
        public void TheFindingCarriesEvidence()
        {
            using var evidence = JsonDocument.Parse(arc.EvidenceAfterOpen);
            Assert.Equal("failed", evidence.RootElement.GetProperty("reason").GetString());
        }
    }

    [Collection(DeadFileLifecycleCollection.Name)]
    public sealed class ScenarioAPassResolvesAFinding(DeadFileLifecycleArc arc)
    {
        // Given an open finding whose predicate no longer holds, When the pass runs.
        [Fact]
        public void TheStateIsResolved()
        {
            Assert.Equal("resolved", arc.StateAfterResolve);
        }

        [Fact]
        public void TheResolvedAtIsSet()
        {
            Assert.NotNull(arc.ResolvedAtAfterResolve);
        }
    }

    [Collection(DeadFileLifecycleCollection.Name)]
    public sealed class ScenarioAResolvedFindingReopens(DeadFileLifecycleArc arc)
    {
        // Given a resolved finding whose predicate holds again, When the pass runs.
        [Fact]
        public void TheStateIsOpenAgain()
        {
            Assert.Equal("open", arc.StateAfterReopen);
        }

        [Fact]
        public void TheSameMediaByKindRowIsReusedNotDuplicated()
        {
            Assert.Equal(arc.FindingIdAfterOpen, arc.FindingIdAfterReopen);
        }
    }

    [Collection(DeadFileLifecycleCollection.Name)]
    public sealed class ScenarioDismissIsForever(DeadFileLifecycleArc arc)
    {
        // Given an open finding, When DismissAsync is called at the store (T372 review MED-3: the
        // STORE half of AC4 — POST /api/gardener/findings/{id}/dismiss itself is T377), then the
        // predicate keeps holding through three more passes.
        [Fact]
        public void TheDismissSucceeds()
        {
            Assert.True(arc.DismissOnOpenSucceeded);
        }

        [Fact]
        public void TheStateStaysDismissedThroughThreePasses()
        {
            Assert.Equal("dismissed", arc.StateAfterDismissAndThreePasses);
        }

        [Fact]
        public void TheDismissedAtIsSet()
        {
            Assert.NotNull(arc.DismissedAtAfterDismissAndThreePasses);
        }

        [Fact]
        public void TheOpenedAtIsUntouchedByThosePasses()
        {
            Assert.Equal(arc.OpenedAtAtDismissTime, arc.OpenedAtAfterDismissAndThreePasses);
        }

        [Fact]
        public void TheResolvedAtStaysNull()
        {
            Assert.Null(arc.ResolvedAtAfterDismissAndThreePasses);
        }

        // Given a RESOLVED finding (not open), When DismissAsync is called at the store.
        [Fact]
        public void DismissingAResolvedRowIsANoOp()
        {
            Assert.False(arc.DismissOnResolvedRowSucceeded);
        }

        // The HTTP half — T377's own endpoint.
        [Fact(Skip = "pending T377 (STORY-374 AC4)")]
        public void TheDismissPostSucceeds() => Assert.Fail("pending T377");
    }

    public sealed class ScenarioTheLoopIsBoundedAndResilient(ThrowingPassArc arc) : IClassFixture<ThrowingPassArc>
    {
        // Given a pass that throws, When the service ticks.
        [Fact]
        public void TheOtherPassesStillRun()
        {
            Assert.True(arc.DeadFileFindingAppeared, "expected the real dead_file pass to still open its finding despite the fake pass throwing");
        }

        [Fact]
        public void OneWarnNamesTheFailedPass()
        {
            var matches = arc.CapturedWarnings.Where(m => m.Contains(ThrowingGardenerPass.KindText, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Single(matches);
        }

        [Fact]
        public void TheNextTickRetries()
        {
            Assert.True(arc.InvocationCountAfterWaiting >= 2, $"expected the fake pass to run again on the next tick; ran {arc.InvocationCountAfterWaiting} time(s)");
        }
    }

    public sealed class ScenarioAPassThatHangsIsBounded(HangingPassArc arc) : IClassFixture<HangingPassArc>
    {
        // T372 review LOW-3 — given a pass that never voluntarily completes, When the service ticks
        // past the current interval.
        [Fact]
        public void TheOtherPassesStillRun()
        {
            Assert.True(arc.DeadFileFindingAppeared, "expected the real dead_file pass to still open its finding despite the hanging pass");
        }

        [Fact]
        public void OneWarnNamesTheHungPass()
        {
            var matches = arc.CapturedWarnings.Where(m => m.Contains(HangingGardenerPass.KindText, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Single(matches);
        }

        [Fact]
        public void TheNextTickIsNotWedged()
        {
            Assert.True(arc.InvocationCountAfterWaiting >= 2, $"expected the hanging pass to be invoked again on the next tick (proving tick 1's own hang never wedged the loop); invoked {arc.InvocationCountAfterWaiting} time(s)");
        }
    }

    public sealed class ScenarioTheServiceRunsInTheProductionBinary(ProductionBinaryArc arc) : IClassFixture<ProductionBinaryArc>
    {
        // Given the api container started with IntervalMinutes 1 and a failed row, When two minutes pass.
        [Fact]
        public void ADeadFileFindingExistsForTheFailedRow()
        {
            Assert.True(arc.FindingAppeared, "expected a dead_file finding for the failed row within two minutes");
        }
    }

    public sealed class ScenarioFindingsAreListedGrouped
    {
        // Given open findings of three kinds, two near-duplicates sharing a group_key, When GET /api/gardener/findings is called.
        [Fact(Skip = "pending T377 (STORY-374 AC7)")]
        public void TheResponseGroupsFindingsByKind() => Assert.Fail("pending T377");

        [Fact(Skip = "pending T377 (STORY-374 AC7)")]
        public void TheDuplicateGroupListsBothMembersWithPathDurationPlaysAndRating() => Assert.Fail("pending T377");
    }

    public sealed class ScenarioStatusCounts
    {
        // Given the findings above, When GET /api/status is called.
        [Fact(Skip = "pending T377 (STORY-374 AC8)")]
        public void TheGardenerSectionCarriesOpenCountsPerKind() => Assert.Fail("pending T377");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the admin surface gates the queue
    // ---------------------------------------------------------------------

    public sealed class ScenarioAdminSurface
    {
        // Given no session, When GET /api/gardener/findings is called.
        [Fact(Skip = "pending T377 (STORY-374 AC10)")]
        public void TheResponseIsFourOhOne() => Assert.Fail("pending T377");
    }
}

// ── Collection definition — AC1/AC2/AC3 share ONE ephemeral Postgres/factory (the Story367
// "each Scenario group arranges its own Postgres exactly once" idiom, via ICollectionFixture<T>). ──

[CollectionDefinition(Name)]
public sealed class DeadFileLifecycleCollection : ICollectionFixture<DeadFileLifecycleArc>
{
    public const string Name = "Story372DeadFileLifecycle";
}

// ── Arc fixtures ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// AC1-AC3: one media row driven through failed → (fixed) → failed-again, running the REAL,
/// container-composed dead_file <see cref="IGardenerPass"/> directly (no hosted-service timer)
/// between each transition — proves open-with-evidence, resolve-with-timestamp, and re-open-reusing-
/// the-same-row in one arrangement.
/// </summary>
public sealed class DeadFileLifecycleArc : IAsyncLifetime
{
    public string StateAfterOpen { get; private set; } = "";
    public string EvidenceAfterOpen { get; private set; } = "";
    public string StateAfterResolve { get; private set; } = "";
    public DateTimeOffset? ResolvedAtAfterResolve { get; private set; }
    public string StateAfterReopen { get; private set; } = "";
    public long FindingIdAfterOpen { get; private set; }
    public long FindingIdAfterReopen { get; private set; }

    // AC4 (T372 review MED-3) — the fourth leg, at the STORE level only (the HTTP endpoint is T377).
    public bool DismissOnResolvedRowSucceeded { get; private set; }
    public bool DismissOnOpenSucceeded { get; private set; }
    public DateTimeOffset OpenedAtAtDismissTime { get; private set; }
    public string StateAfterDismissAndThreePasses { get; private set; } = "";
    public DateTimeOffset? DismissedAtAfterDismissAndThreePasses { get; private set; }
    public DateTimeOffset OpenedAtAfterDismissAndThreePasses { get; private set; }
    public DateTimeOffset? ResolvedAtAfterDismissAndThreePasses { get; private set; }

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field — Story372GardenerDatabase is file-local (CS9051), the identical
        // reason Story367's own arcs give for the same shape.
        await using var database = await Story372GardenerDatabase.StartAsync();

        var mediaId = await GardenerRotFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/t372-lifecycle.flac", "failed");

        await using var factory = new Story372DirectPassWebFactory(database);
        var pass = factory.Services.GetServices<IGardenerPass>().Single(p => p.Kind == RotKind.DeadFile);
        var store = factory.Services.GetRequiredService<IRotFindingStore>();

        // AC1 — the predicate holds, no finding yet.
        await pass.RunAsync(CancellationToken.None);
        var afterOpen = await GardenerRotFixtures.ReadFindingAsync(database.LibraryConnectionString, mediaId, "dead_file")
            ?? throw new InvalidOperationException("expected a dead_file finding after the first pass run");
        FindingIdAfterOpen = afterOpen.Id;
        StateAfterOpen = afterOpen.State;
        EvidenceAfterOpen = afterOpen.Evidence;

        // AC2 — fix the row; the predicate stops holding.
        await GardenerRotFixtures.SetMediaStateAsync(database.LibraryConnectionString, mediaId, "ready");
        await pass.RunAsync(CancellationToken.None);
        var afterResolve = await GardenerRotFixtures.ReadFindingAsync(database.LibraryConnectionString, mediaId, "dead_file")
            ?? throw new InvalidOperationException("expected the finding to still exist (resolved) after the second pass run");
        StateAfterResolve = afterResolve.State;
        ResolvedAtAfterResolve = afterResolve.ResolvedAt;

        // AC4's second leg — DismissAsync on a RESOLVED row is a no-op (only an OPEN row can be
        // dismissed): proven HERE, on the resolved row from AC2, before AC3 ever re-opens it, so the
        // no-op is genuinely observed against a resolved row rather than an open one.
        DismissOnResolvedRowSucceeded = await store.DismissAsync(afterResolve.Id, CancellationToken.None);

        // AC3 — break it again; the predicate holds once more.
        await GardenerRotFixtures.SetMediaStateAsync(database.LibraryConnectionString, mediaId, "failed");
        await pass.RunAsync(CancellationToken.None);
        var afterReopen = await GardenerRotFixtures.ReadFindingAsync(database.LibraryConnectionString, mediaId, "dead_file")
            ?? throw new InvalidOperationException("expected the finding to exist (re-opened) after the third pass run");
        StateAfterReopen = afterReopen.State;
        FindingIdAfterReopen = afterReopen.Id;

        // AC4's first leg — dismiss the now-OPEN finding at the store, then run the pass three MORE
        // times with the predicate still holding (the media row is still 'failed'): a dismissed row
        // must stay dismissed, its dismissed_at set once, and its opened_at/resolved_at left exactly
        // as they were the instant it was dismissed (T372 review MED-3).
        DismissOnOpenSucceeded = await store.DismissAsync(afterReopen.Id, CancellationToken.None);
        var atDismiss = await GardenerRotFixtures.ReadFindingAsync(database.LibraryConnectionString, mediaId, "dead_file")
            ?? throw new InvalidOperationException("expected the finding to still exist immediately after dismiss");
        OpenedAtAtDismissTime = atDismiss.OpenedAt;

        for (var i = 0; i < 3; i++)
            await pass.RunAsync(CancellationToken.None);

        var afterDismissAndPasses = await GardenerRotFixtures.ReadFindingAsync(database.LibraryConnectionString, mediaId, "dead_file")
            ?? throw new InvalidOperationException("expected the finding to still exist after three more passes");
        StateAfterDismissAndThreePasses = afterDismissAndPasses.State;
        DismissedAtAfterDismissAndThreePasses = afterDismissAndPasses.DismissedAt;
        OpenedAtAfterDismissAndThreePasses = afterDismissAndPasses.OpenedAt;
        ResolvedAtAfterDismissAndThreePasses = afterDismissAndPasses.ResolvedAt;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC5: a fake, throwing <see cref="IGardenerPass"/> on the real, ticking <c>GardenerService</c>
/// (kept alive by name, every other hosted service removed). T375 review MED-2: a REAL
/// <see cref="GenWave.MediaLibrary.Garden.StaleMetadataGardenerPass"/> now shares this container,
/// so <c>RemoveAll&lt;IGardenerPass&gt;()</c> clears every production pass BEFORE this arc
/// registers its own two fakes — the throwing one below, plus
/// <see cref="SucceedingDeadFileGardenerPass"/> standing in for "some other pass in the loop" —
/// so the resilience loop under test composes ONLY test doubles, never a real pass whose own
/// behaviour could shift under a future task. One seeded failed row proves the stand-in still
/// opened its finding, exactly one captured WARN names the fake pass's own kind, and waiting past
/// a second tick proves the fake pass ran again (the loop never gives up on a repeatedly-failing
/// pass).
/// </summary>
public sealed class ThrowingPassArc : IAsyncLifetime
{
    public bool DeadFileFindingAppeared { get; private set; }
    public IReadOnlyList<string> CapturedWarnings { get; private set; } = [];
    public int InvocationCountAfterWaiting { get; private set; }

    public async Task InitializeAsync()
    {
        // Both LOCALS, not fields — Story372GardenerDatabase/Story372LiveServiceWebFactory are
        // file-local (CS9051, the identical reason Story367's own arcs give); every value this arc
        // exposes is captured into a property below BEFORE either is torn down at the end of this
        // method (the same "await using var ... = ..." shape every other arc in this suite uses) —
        // the real GardenerService keeps ticking for the FULL duration of the waits below, since
        // both waits are awaited here, inside this very block, before the factory ever disposes.
        await using var database = await Story372GardenerDatabase.StartAsync();
        var mediaId = await GardenerRotFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/t372-throwing-pass.flac", "failed");

        var fakePass = new ThrowingGardenerPass();
        var logs = new CapturingWarningLoggerProvider();

        var settings = new Dictionary<string, string?>
        {
            ["Gardener:IntervalMinutes"] = "1",
            ["Library:ScanIntervalSeconds"] = "2",
        };

        await using var factory = new Story372LiveServiceWebFactory(database, settings, services =>
        {
            // T375 review MED-2: strip every production IGardenerPass FIRST — the resilience loop
            // this arc drives is a GardenerService fact, not a real-pass fact, so it must compose
            // ONLY test doubles regardless of how many real passes the container registers.
            services.RemoveAll<IGardenerPass>();
            services.AddSingleton<IGardenerPass>(
                sp => new SucceedingDeadFileGardenerPass(sp.GetRequiredService<IRotFindingStore>(), mediaId));
            services.AddSingleton<IGardenerPass>(fakePass);
            services.AddSingleton<ILoggerProvider>(logs);
        });

        // Touching Services triggers ConfigureWebHost + the real host start (Story297's own
        // "touching Services is what triggers the callback" precedent) — GardenerService's real
        // PeriodicTimer starts running here.
        _ = factory.Services;

        DeadFileFindingAppeared = await GardenerRotFixtures.WaitForFindingAsync(
            database.LibraryConnectionString, mediaId, "dead_file", TimeSpan.FromSeconds(30));

        // Snapshot the WARN log right after the FIRST tick (STORY-374 AC5: "one WARN names the
        // failed pass" is a per-tick fact) — captured BEFORE waiting for the second tick below,
        // which would otherwise add a second, entirely expected WARN of its own and make a
        // "the log has exactly one warning ever" assertion fail for the wrong reason.
        await GardenerRotFixtures.WaitUntilAsync(() => fakePass.InvocationCount >= 1, TimeSpan.FromSeconds(30));
        CapturedWarnings = logs.Messages;

        await GardenerRotFixtures.WaitUntilAsync(() => fakePass.InvocationCount >= 2, TimeSpan.FromSeconds(100));
        InvocationCountAfterWaiting = fakePass.InvocationCount;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// T372 review LOW-3's own pin: a fake <see cref="IGardenerPass"/> that never voluntarily completes
/// — proves the per-pass bounded <see cref="CancellationTokenSource"/> (linked to the current
/// interval) is what actually ends it, not a cooperative early exit the pass itself never offers.
/// T375 review MED-2: the SAME <c>RemoveAll&lt;IGardenerPass&gt;()</c> + <see cref="SucceedingDeadFileGardenerPass"/>
/// stand-in <see cref="ThrowingPassArc"/>'s own remarks explain — this arc's real, ticking
/// <c>GardenerService</c> also composes ONLY test doubles, never a real pass. One WARN names the
/// hanging pass after the current interval elapses, the stand-in still opens its own finding in the
/// SAME tick, and a SECOND invocation (proven by waiting past a second tick) is the direct evidence
/// tick 1's own hang never wedged tick 2.
/// </summary>
public sealed class HangingPassArc : IAsyncLifetime
{
    public bool DeadFileFindingAppeared { get; private set; }
    public IReadOnlyList<string> CapturedWarnings { get; private set; } = [];
    public int InvocationCountAfterWaiting { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await Story372GardenerDatabase.StartAsync();
        var mediaId = await GardenerRotFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/t372-hanging-pass.flac", "failed");

        var fakePass = new HangingGardenerPass();
        var logs = new CapturingWarningLoggerProvider();

        var settings = new Dictionary<string, string?>
        {
            ["Gardener:IntervalMinutes"] = "1",
            ["Library:ScanIntervalSeconds"] = "2",
        };

        await using var factory = new Story372LiveServiceWebFactory(database, settings, services =>
        {
            // T375 review MED-2: see ThrowingPassArc's own remarks — strip every production
            // IGardenerPass, then compose ONLY this arc's own two test doubles.
            services.RemoveAll<IGardenerPass>();
            services.AddSingleton<IGardenerPass>(
                sp => new SucceedingDeadFileGardenerPass(sp.GetRequiredService<IRotFindingStore>(), mediaId));
            services.AddSingleton<IGardenerPass>(fakePass);
            services.AddSingleton<ILoggerProvider>(logs);
        });

        _ = factory.Services;

        DeadFileFindingAppeared = await GardenerRotFixtures.WaitForFindingAsync(
            database.LibraryConnectionString, mediaId, "dead_file", TimeSpan.FromSeconds(30));

        // Tick 1's own timeout WARN — the hanging pass's budget is the CURRENT interval
        // (Gardener:IntervalMinutes floored at 60s). Snapshot right after the first invocation is
        // observed AND its WARN has landed, before waiting for a second tick (the SAME "capture
        // before the next tick adds its own WARN" discipline ThrowingPassArc's own remarks explain).
        await GardenerRotFixtures.WaitUntilAsync(
            () => fakePass.InvocationCount >= 1
                && logs.Messages.Any(m => m.Contains(HangingGardenerPass.KindText, StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(90));
        CapturedWarnings = logs.Messages;

        // Tick 2 reaching the hanging pass again is the direct proof tick 1's own hang never wedged
        // the loop.
        await GardenerRotFixtures.WaitUntilAsync(() => fakePass.InvocationCount >= 2, TimeSpan.FromSeconds(100));
        InvocationCountAfterWaiting = fakePass.InvocationCount;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// AC6: the ONE exhibit that must run the real <c>GardenerService</c> unattended inside the
/// production binary rather than driving a pass directly — <c>Gardener:IntervalMinutes=1</c> plus a
/// short <c>Library:ScanIntervalSeconds</c> startup delay (GardenerService's own honest first-tick
/// test seam) against a single seeded failed row; polls up to two minutes of genuine wall-clock for
/// the dead_file finding to appear.
/// </summary>
public sealed class ProductionBinaryArc : IAsyncLifetime
{
    public bool FindingAppeared { get; private set; }

    public async Task InitializeAsync()
    {
        await using var database = await Story372GardenerDatabase.StartAsync();
        var mediaId = await GardenerRotFixtures.InsertMediaRowAsync(database.LibraryConnectionString, "/test/t372-production-binary.flac", "failed");

        var settings = new Dictionary<string, string?>
        {
            ["Gardener:IntervalMinutes"] = "1",
            ["Library:ScanIntervalSeconds"] = "2",
        };

        await using var factory = new Story372LiveServiceWebFactory(database, settings);
        _ = factory.Services;

        FindingAppeared = await GardenerRotFixtures.WaitForFindingAsync(
            database.LibraryConnectionString, mediaId, "dead_file", TimeSpan.FromMinutes(2));
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Test doubles ─────────────────────────────────────────────────────────────────────────────────

/// <summary>T375 review MED-2: <see cref="ThrowingPassArc"/>/<see cref="HangingPassArc"/> both
/// <c>RemoveAll&lt;IGardenerPass&gt;()</c> before registering their own fakes, so this type — not
/// the real <c>DeadFileGardenerPass</c> — is what proves "some OTHER pass in the loop still runs"
/// despite a throwing/hanging sibling. Reuses <see cref="IRotFindingStore.OpenDeadFileAsync"/>
/// (T373's own single-row report seam) rather than reimplementing <c>DeadFileGardenerPass</c>'s
/// own reconcile predicate — a deliberately trivial double, never a second copy of production
/// logic.</summary>
file sealed class SucceedingDeadFileGardenerPass(IRotFindingStore store, long mediaId) : IGardenerPass
{
    public RotKind Kind => RotKind.DeadFile;

    public Task RunAsync(CancellationToken ct) => store.OpenDeadFileAsync(mediaId, "test-stand-in", ct);
}

/// <summary>AC5's own fake: always throws, standing in for a real pass failure; carries its own
/// invocation count so <see cref="ThrowingPassArc"/> can prove the loop retries it on the next
/// tick. <see cref="Kind"/>'s value is arbitrary (T375 review MED-2) — the arc's own
/// <c>RemoveAll&lt;IGardenerPass&gt;()</c> guarantees no real pass shares this container, so no
/// <see cref="RotKind"/> choice can collide with one; <see cref="RotKind.StaleMetadata"/> is kept
/// only because it was already here.</summary>
file sealed class ThrowingGardenerPass : IGardenerPass
{
    public const string KindText = nameof(RotKind.StaleMetadata);

    int invocationCount;

    public int InvocationCount => Volatile.Read(ref invocationCount);

    public RotKind Kind => RotKind.StaleMetadata;

    public Task RunAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref invocationCount);
        throw new InvalidOperationException("simulated gardener pass failure (STORY-374 AC5)");
    }
}

/// <summary>T372 review LOW-3's own fake: never voluntarily completes — <see cref="Task.Delay(int, CancellationToken)"/>
/// with <see cref="Timeout.Infinite"/> using the SAME <c>ct</c> GardenerService hands it (its own
/// per-pass linked <see cref="CancellationTokenSource"/>), never the outer shutdown token directly —
/// this is what proves GardenerService's OWN bounded timeout ends it, not a cooperative early exit
/// this pass never offers on its own. <see cref="Kind"/>'s value is arbitrary (T375 review MED-2,
/// the SAME <see cref="ThrowingGardenerPass"/> rationale) — <see cref="HangingPassArc"/>'s own
/// <c>RemoveAll&lt;IGardenerPass&gt;()</c> guarantees no real pass, and no other fake in THIS arc,
/// shares this container; <see cref="RotKind.Unreachable"/> is kept only because it was already
/// here.
/// </summary>
file sealed class HangingGardenerPass : IGardenerPass
{
    public const string KindText = nameof(RotKind.Unreachable);

    int invocationCount;

    public int InvocationCount => Volatile.Read(ref invocationCount);

    public RotKind Kind => RotKind.Unreachable;

    public async Task RunAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref invocationCount);
        await Task.Delay(Timeout.Infinite, ct);
    }
}

/// <summary>Captures every Warning+ log entry's message text — the
/// Story164_FailClosedWithoutPassword.cs/Story367_TheStationRemembersEveryAiring.cs
/// CapturingWarningLoggerProvider precedent, redefined here (file-scoped, "no shared test-support
/// project exists" acceptance applied again).</summary>
file sealed class CapturingWarningLoggerProvider : ILoggerProvider
{
    readonly List<string> messages = [];
    public IReadOnlyList<string> Messages { get { lock (messages) return messages.ToList(); } }

    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }

    void Add(string message) { lock (messages) messages.Add(message); }

    sealed class Logger(CapturingWarningLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) owner.Add(formatter(state, exception));
        }
    }
}

// ── Test harness — WebApplicationFactory subclasses ─────────────────────────────────────────────

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres, with every hosted
/// service removed (no Liquidsoap/real-background-loop reach) — the seam AC1-AC3 need: the real,
/// container-composed <see cref="IGardenerPass"/> fan-out is still resolvable and callable directly,
/// mirroring Story367's own <c>MediaRotationDrainService</c> direct-call idiom.
/// </summary>
file sealed class Story372DirectPassWebFactory(Story372GardenerDatabase db) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", "test-password-t372-gardener-direct");
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
/// Boots the real production composition root with EVERY hosted service removed EXCEPT
/// <c>GardenerService</c> — kept alive by NAME (captured before <c>RemoveAll&lt;IHostedService&gt;()</c>
/// and re-added), since it is internal to <c>GenWave.MediaLibrary</c> and this test assembly carries
/// no <c>InternalsVisibleTo</c> there (mirrors Story297_ContextTickerWire.cs's own capture-then-readd
/// idiom, one step further: that file could re-add its target BY TYPE because it lives in
/// <c>GenWave.Host</c> itself).
/// </summary>
file sealed class Story372LiveServiceWebFactory(
    Story372GardenerDatabase db,
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? extraConfigure = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", "test-password-t372-gardener-live");
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");

        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            var gardenerDescriptors = services
                .Where(sd => sd.ServiceType == typeof(IHostedService) && sd.ImplementationType?.Name == "GardenerService")
                .ToList();

            services.RemoveAll<IHostedService>();
            foreach (var descriptor in gardenerDescriptors)
                services.Add(descriptor);

            extraConfigure?.Invoke(services);
        });
    }
}

/// <summary>
/// This file's own thin subclass of the shared <see cref="EphemeralStationDatabase"/> harness (see
/// <c>GardenerSeedTestDatabase</c>/<c>SensorGateStationDatabase</c>'s own remarks for the full
/// "which compose file, why a unique project name + OS-assigned port" rationale). Supplies only the
/// <c>"genwave-t372a"</c> compose project-name prefix this file's own arcs need — kept short so
/// <c>Provision</c>'s own 24-char project-name cap still leaves real GUID entropy after the prefix
/// (a longer prefix here once truncated the GUID to nothing, and every arc collided on the SAME
/// container name under parallel execution).
/// </summary>
file sealed class Story372GardenerDatabase : EphemeralStationDatabase
{
    Story372GardenerDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<Story372GardenerDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-t372a");
        var db = new Story372GardenerDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>Arrange/read helpers this file's own arcs share — raw SQL against the ephemeral
/// database's own connection string, never through <c>RotFindingRepository</c> itself (that would
/// only prove the repository agrees with itself; these arcs need an independent read of what
/// actually landed in Postgres). Mirrors <c>GardenerSeedFixtures</c>' own precedent
/// (Story367_TheStationRemembersEveryAiring.cs), redefined here per that file's own "no shared
/// test-support project" duplication-by-necessity acceptance.</summary>
public static class GardenerRotFixtures
{
    public readonly record struct FindingRow(
        long Id, string State, string Evidence, DateTimeOffset OpenedAt, DateTimeOffset? ResolvedAt, DateTimeOffset? DismissedAt);

    public static async Task<long> InsertMediaRowAsync(
        string libraryConnectionString, string path, string state, DateTimeOffset? unavailableSince = null)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            insert into library.media (path, format, size_bytes, mtime, state, unavailable_since)
            values (@path, 'flac', 1024, now(), @state, @unavailableSince)
            returning id
            """;
        cmd.Parameters.AddWithValue("path", path);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("unavailableSince", (object?)unavailableSince ?? DBNull.Value);
        return (long)(await cmd.ExecuteScalarAsync() ?? throw new InvalidOperationException("insert returned no id"));
    }

    public static async Task SetMediaStateAsync(string libraryConnectionString, long mediaId, string state)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "update library.media set state = @state where id = @mediaId";
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<FindingRow?> ReadFindingAsync(string libraryConnectionString, long mediaId, string kindText)
    {
        await using var conn = new NpgsqlConnection(libraryConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            select id, state::text, evidence::text, opened_at, resolved_at, dismissed_at
            from library.rot_finding
            where media_id = @mediaId and kind = @kind::library.rot_kind
            """;
        cmd.Parameters.AddWithValue("mediaId", mediaId);
        cmd.Parameters.AddWithValue("kind", kindText);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new FindingRow(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }

    /// <summary>Polls for an OPEN <paramref name="kindText"/> finding on <paramref name="mediaId"/>
    /// up to <paramref name="timeout"/> of genuine wall-clock — the honest way to observe a real,
    /// ticking <c>GardenerService</c> rather than calling a pass directly.</summary>
    public static async Task<bool> WaitForFindingAsync(
        string libraryConnectionString, long mediaId, string kindText, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var finding = await ReadFindingAsync(libraryConnectionString, mediaId, kindText);
            if (finding is { State: "open" }) return true;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return false;
    }

    /// <summary>Generic poll-until, shared by every Arc in this file that watches an in-memory
    /// condition (an invocation counter, a captured log line) rather than a database row — the same
    /// 1-second-granularity wall-clock wait <see cref="WaitForFindingAsync"/> uses for its own
    /// database poll.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
