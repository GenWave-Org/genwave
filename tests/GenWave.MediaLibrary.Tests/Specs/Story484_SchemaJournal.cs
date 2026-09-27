// STORY-484 — The schema journal (gh-#868 · SPEC F211.3–F211.4 · PLAN T591, T592)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration). RED at plan time: every fact is
// [Fact(Skip = Pending)] with a loud body — remove the Skip only in the task that makes it green.
// Each Given comment names the arrange the scenario needs.
// Entry point: the real ./migrate.sh against a throwaway compose db (T591); the Postgres journal
// reader via DatabaseCollection (T592).
//
// Fixture shape: each scenario's arrange runs ONCE (an xUnit Fact class fixture per scenario — same
// "Arc" pattern as Story367_TheStationRemembersEveryAiring.cs's LedgerSeedArc/SeedIdempotencyArc), not
// once per [Fact] — xUnit constructs a new scenario-class instance per Fact, so putting IAsyncLifetime
// directly on the scenario class re-runs migrate.sh (and re-provisions a whole compose db) per assert.

using GenWave.Host.Tests.Support;
using GenWave.MediaLibrary.Tests.Support;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureSchemaJournal
{
    const string PendingReader = "pending: T592 — ISchemaJournal reader (STORY-484)";

    /// <summary>The planted, always-failing migration AC9's fail-fast scenario plants — sorts LAST (after every real script), so fail-fast and --keep-going look identical through it (both run every real script first, then hit the failure).</summary>
    internal const string FailingScript = "50-spec-fail-migration.sh";

    /// <summary>
    /// The planted, always-failing migration the --keep-going scenario plants. Sorts BEFORE db/49 (the
    /// glob <c>db/*-migration.sh</c> in migrate.sh expands in plain byte order, where <c>'z'</c> &gt;
    /// <c>'-'</c>, so <c>48z-...</c> falls between <c>48-...</c> and <c>49-...</c>) — unlike
    /// <see cref="FailingScript"/>, this name proves --keep-going actually keeps going, since fail-fast
    /// would stop here and never reach db/49 at all.
    /// </summary>
    internal const string KeepGoingFailingScript = "48z-spec-fail-migration.sh";

    static string[] RealMigrationScripts() =>
        Directory.GetFiles(Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "db"), "*-migration.sh")
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A scratch repo copy (Support/TempDir): migrate.sh and every real db/*-migration.sh symlinked in,
    /// plus one planted script named <paramref name="failingScript"/> that always exits 1 — AC9's "a
    /// failed script writes nothing" case (<see cref="FailingScript"/>) and the --keep-going scenario's
    /// own name (<see cref="KeepGoingFailingScript"/>) both call this with their own planted name.
    /// </summary>
    internal static string MakeScratchWithFailingMigration(string failingScript)
    {
        var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);
        var scratch = TempDir.CreateForProcessLifetime();
        File.CreateSymbolicLink(Path.Combine(scratch, "migrate.sh"), Path.Combine(repoRoot, "migrate.sh"));

        var scratchDb = Path.Combine(scratch, "db");
        Directory.CreateDirectory(scratchDb);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "db"), "*-migration.sh"))
            File.CreateSymbolicLink(Path.Combine(scratchDb, Path.GetFileName(file)), file);

        File.WriteAllText(Path.Combine(scratchDb, failingScript),
            "#!/usr/bin/env bash\n# STORY-484 — planted; always fails, never journalled.\nexit 1\n");

        return scratch;
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrateShOnAPreF211Db(PreF211JournalFixture fixture) : IClassFixture<PreF211JournalFixture>
    {
        // Given: a throwaway compose db with no station.schema_migration; GW_VERSION=v5.14.0; ./migrate.sh

        /// <summary>
        /// AC1 — station.schema_migration exists with (script pk, applied_at not null, app_version
        /// null-able). "script" being the primary key specifically (not merely NOT NULL) is proven
        /// separately by the <c>ON CONFLICT (script)</c> upsert exercised in AC2/AC4 below — a second
        /// migrate.sh run only updates in place, rather than erroring or duplicating rows, precisely
        /// because Postgres accepted that column as the PK.
        /// </summary>
        [Fact]
        public void CreatesTheJournal() =>
            Assert.Equal(
                new[]
                {
                    ("script", "text", "NO"),
                    ("applied_at", "timestamp with time zone", "NO"),
                    ("app_version", "text", "YES"),
                },
                fixture.Columns);

        /// <summary>AC2 — one row per db/*-migration.sh, keyed by file name</summary>
        [Fact]
        public void OneRowPerScript() =>
            Assert.Equal(RealMigrationScripts(), fixture.Journal.Select(row => row.Script).OrderBy(name => name, StringComparer.Ordinal));

        /// <summary>AC3 — every row's app_version is "v5.14.0"</summary>
        [Fact]
        public void StampsTheAppVersion() => Assert.All(fixture.Journal, row => Assert.Equal(PreF211JournalFixture.Version, row.AppVersion));
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioMigrateShRunsTwice(RunsTwiceFixture fixture) : IClassFixture<RunsTwiceFixture>
    {
        // Given: the same db, ./migrate.sh run a second time

        /// <summary>AC4 — the row count is unchanged</summary>
        [Fact]
        public void RowCountUnchanged() => Assert.Equal(fixture.FirstRun.Count, fixture.SecondRun.Count);

        /// <summary>AC4 — applied_at on 49-ad-spot-auto-rerender-marker-migration.sh moved forward</summary>
        [Fact]
        public void AppliedAtMovesForward()
        {
            var before = fixture.FirstRun.Single(row => row.Script == RunsTwiceFixture.MarkerScript).AppliedAt;
            var after = fixture.SecondRun.Single(row => row.Script == RunsTwiceFixture.MarkerScript).AppliedAt;
            Assert.True(after > before, $"expected {after} > {before}");
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFreshInit(DatabaseFixture db)
    {
        // Given: the fixture's freshly initialised db (db/06 ran)

        /// <summary>AC5 — station.schema_migration exists (gh-#618 fresh-init mirror)</summary>
        [Fact]
        public void TableExists() => Assert.Contains(("station", "schema_migration", "script"), db.InitialSchema.Keys);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAppliedIsTheHighestJournalledNumber(DatabaseFixture db)
    {
        // Given: journal rows for 47-, 48- and 49-*-migration.sh; the reader's GetAppliedAsync

        /// <summary>AC7 — Applied is 49</summary>
        [Fact(Skip = PendingReader)]
        public void AppliedIs49() => Assert.Fail($"{PendingReader} {db}");
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioNoGwVersion(NoGwVersionFixture fixture) : IClassFixture<NoGwVersionFixture>
    {
        // Given: a throwaway compose db; GW_VERSION unset; ./migrate.sh

        /// <summary>AC8 — app_version is null on every row</summary>
        [Fact]
        public void AppVersionIsNull() => Assert.All(fixture.Journal, row => Assert.Null(row.AppVersion));
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioAFailedScript(FailedScriptFixture fixture) : IClassFixture<FailedScriptFixture>
    {
        // Given: a temp db/ copy (Support/TempDir) with a 50-*-migration.sh that exits 1; ./migrate.sh

        /// <summary>AC9 — no row for the failed script</summary>
        [Fact]
        public void FailedScriptNotJournalled() => Assert.DoesNotContain(fixture.Journal, row => row.Script == FailingScript);

        /// <summary>AC9 — every script before it is journalled</summary>
        [Fact]
        public void EarlierScriptsJournalled() =>
            Assert.Empty(RealMigrationScripts().Except(fixture.Journal.Select(row => row.Script)));
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioKeepGoingContinuesPastTheFailure(KeepGoingFixture fixture) : IClassFixture<KeepGoingFixture>
    {
        // Given: a planted failing 48z-spec-fail-migration.sh — sorts BEFORE db/49 — run with ./migrate.sh --keep-going

        /// <summary>--keep-going: no row for the failed script itself</summary>
        [Fact]
        public void FailedScriptNotJournalled() =>
            Assert.DoesNotContain(fixture.Journal, row => row.Script == FeatureSchemaJournal.KeepGoingFailingScript);

        /// <summary>--keep-going: a real script sorted AFTER the failure (db/49) is still journalled — the fact fail-fast would not produce, since fail-fast stops at the failure and never reaches db/49</summary>
        [Fact]
        public void LaterScriptStillJournalled() =>
            Assert.Contains(fixture.Journal, row => row.Script == RunsTwiceFixture.MarkerScript);

        /// <summary>--keep-going: every real script, before or after the failure, ends up journalled</summary>
        [Fact]
        public void EveryRealScriptJournalled() =>
            Assert.Empty(RealMigrationScripts().Except(fixture.Journal.Select(row => row.Script)));
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnEmptyJournal(DatabaseFixture db)
    {
        // Given: station.schema_migration truncated; GetAppliedAsync

        /// <summary>AC10 — Applied is null</summary>
        [Fact(Skip = PendingReader)]
        public void AppliedIsNull() => Assert.Fail($"{PendingReader} {db}");
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNoJournalTable(DatabaseFixture db)
    {
        // Given: station.schema_migration dropped (pre-F211 db); GetAppliedAsync

        /// <summary>AC10 (STORY-485) — Applied is null, no throw (42P01 swallowed)</summary>
        [Fact(Skip = PendingReader)]
        public void AppliedIsNull() => Assert.Fail($"{PendingReader} {db}");
    }
}

/// <summary>AC1–AC3 arrange: a fresh compose db with no station.schema_migration, migrated once with GW_VERSION set.</summary>
public sealed class PreF211JournalFixture : IAsyncLifetime
{
    public const string Version = "v5.14.0";

    public IReadOnlyList<(string Name, string DataType, string IsNullable)> Columns { get; private set; } = [];
    public IReadOnlyList<(string Script, DateTimeOffset AppliedAt, string? AppVersion)> Journal { get; private set; } = [];

    public async Task InitializeAsync()
    {
        // A LOCAL, not a field (same shape as Story367_TheStationRemembersEveryAiring.cs's LedgerSeedArc):
        // every value this fixture exposes is captured into a property before the container tears down.
        await using var db = await MigrateShDatabase.StartAsync();
        await db.DropSchemaJournalAsync();

        var run = db.RunMigrateSh(gwVersion: Version);
        if (run.ExitCode != 0)
            throw new InvalidOperationException($"migrate.sh failed (exit {run.ExitCode}):\n{run.StdOut}\n{run.StdErr}");

        Columns = await db.ReadJournalColumnsAsync();
        Journal = await db.ReadJournalAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>AC4 arrange: the same db, migrated twice a second apart.</summary>
public sealed class RunsTwiceFixture : IAsyncLifetime
{
    public const string MarkerScript = "49-ad-spot-auto-rerender-marker-migration.sh";

    public IReadOnlyList<(string Script, DateTimeOffset AppliedAt, string? AppVersion)> FirstRun { get; private set; } = [];
    public IReadOnlyList<(string Script, DateTimeOffset AppliedAt, string? AppVersion)> SecondRun { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var db = await MigrateShDatabase.StartAsync();

        var first = db.RunMigrateSh();
        if (first.ExitCode != 0)
            throw new InvalidOperationException($"migrate.sh (first run) failed (exit {first.ExitCode}):\n{first.StdOut}\n{first.StdErr}");
        FirstRun = await db.ReadJournalAsync();

        // timestamptz/now() has microsecond precision, so back-to-back runs would already produce
        // distinct applied_at values without any delay — this 1 s wait is only a safety margin against
        // clock-resolution or scheduling noise on a loaded box, not a requirement of the column type.
        await Task.Delay(TimeSpan.FromSeconds(1));

        var second = db.RunMigrateSh();
        if (second.ExitCode != 0)
            throw new InvalidOperationException($"migrate.sh (second run) failed (exit {second.ExitCode}):\n{second.StdOut}\n{second.StdErr}");
        SecondRun = await db.ReadJournalAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>AC8 arrange: a fresh compose db, migrated once with GW_VERSION left unset.</summary>
public sealed class NoGwVersionFixture : IAsyncLifetime
{
    public IReadOnlyList<(string Script, DateTimeOffset AppliedAt, string? AppVersion)> Journal { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var db = await MigrateShDatabase.StartAsync();

        var run = db.RunMigrateSh();
        if (run.ExitCode != 0)
            throw new InvalidOperationException($"migrate.sh failed (exit {run.ExitCode}):\n{run.StdOut}\n{run.StdErr}");

        Journal = await db.ReadJournalAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>AC9 arrange: the scratch repo with a planted failing 50-*-migration.sh, run fail-fast (the default).</summary>
public sealed class FailedScriptFixture : IAsyncLifetime
{
    public IReadOnlyList<(string Script, DateTimeOffset AppliedAt, string? AppVersion)> Journal { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var db = await MigrateShDatabase.StartAsync();

        var scratchMigrateSh = Path.Combine(
            FeatureSchemaJournal.MakeScratchWithFailingMigration(FeatureSchemaJournal.FailingScript), "migrate.sh");
        var run = db.RunMigrateSh(migrateShPath: scratchMigrateSh);
        if (run.ExitCode == 0)
            throw new InvalidOperationException(
                $"migrate.sh unexpectedly succeeded despite the planted failing script:\n{run.StdOut}\n{run.StdErr}");

        Journal = await db.ReadJournalAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// The same shape of planted failure as <see cref="FailedScriptFixture"/>, but run with --keep-going,
/// and planted under <see cref="FeatureSchemaJournal.KeepGoingFailingScript"/> — a name that sorts
/// BEFORE db/49 — so a real script after the failure point is only journalled if --keep-going actually
/// kept going past it (fail-fast would have stopped there and never reached db/49).
/// </summary>
public sealed class KeepGoingFixture : IAsyncLifetime
{
    public IReadOnlyList<(string Script, DateTimeOffset AppliedAt, string? AppVersion)> Journal { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var db = await MigrateShDatabase.StartAsync();

        var scratchMigrateSh = Path.Combine(
            FeatureSchemaJournal.MakeScratchWithFailingMigration(FeatureSchemaJournal.KeepGoingFailingScript), "migrate.sh");
        var run = db.RunMigrateSh(migrateShPath: scratchMigrateSh, keepGoing: true);
        if (run.ExitCode == 0)
            throw new InvalidOperationException(
                $"migrate.sh --keep-going unexpectedly succeeded despite the planted failing script:\n{run.StdOut}\n{run.StdErr}");

        Journal = await db.ReadJournalAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
