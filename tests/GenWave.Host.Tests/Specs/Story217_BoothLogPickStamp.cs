// STORY-217 — The booth log tells me why each track was picked (SPEC F86.1, F86.2, F86.5, F86.9)
//
// BDD specification — xUnit. PLAN T73 (docs/PLAN.md Phase V24) built the write-side stamp: the
// playout event sink (BoothLogWriter, GenWave.MediaLibrary.Station) stamps booth_log.pick jsonb at
// air time from the SAME PersonaPickDiagnostics the copywriter reads off MediaItem.PersonaPick
// (F83.1) — fired-rule summaries, signed weights, exploration flag. Null for: rows predating the
// column, engine-initiated plays, persona-off picks. Never backfilled (F84.6 precedent).
// Scores/pool/degradation are deliberately NOT stored (F86.1) — assert their absence, not just the
// presence of the rest. T74 wires GET /api/booth-log's own exposure of the stamp — those
// scenarios/facts stay pending below.
//
// Write-side entry-point discipline: BoothLogWriter/BoothLogDrainService/BoothLogEntryRequest are
// internal to GenWave.MediaLibrary (no InternalsVisibleTo to this project) — every write-side
// scenario below drives the REAL production pipeline the exact way GenWave.Host composes it
// (BoothLogServiceCollectionExtensions.AddBoothLog, then IBoothLogEventConsumer/IHostedService),
// with only the repository seam (IBoothLogAppender) faked — mirrors Story195_BoothLog.cs's own
// FakeBoothLogReader idiom on the read side. The API scenario (T74) will drive GET /api/booth-log
// through the production controller pipeline instead (Story123's controller/factory idiom).

using System.Reflection;
using System.Text.Json;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Api;
using GenWave.MediaLibrary.Station;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// In-memory <see cref="IBoothLogAppender"/> double: records every <see cref="AppendAsync"/> call's
/// arguments instead of touching Postgres — the repository seam <see cref="BoothLogWriteHarness"/>
/// fakes (mirrors <c>FakeBoothLogReader</c>'s idiom on the read side, Story195_BoothLog.cs). Releases
/// a signal per call so a scenario can await exactly as many appends as it published, with no
/// arbitrary sleep.
/// </summary>
file sealed class FakeBoothLogAppender : IBoothLogAppender
{
    readonly SemaphoreSlim appended = new(0);

    public List<(string Kind, string Summary, long? PersonaId, string? Artist, string? Pick)> Calls { get; } = [];

    public Task AppendAsync(string kind, string summary, long? personaId, string? artist, string? pick, CancellationToken ct)
    {
        lock (Calls) Calls.Add((kind, summary, personaId, artist, pick));
        appended.Release();
        return Task.CompletedTask;
    }

    public async Task WaitForCallsAsync(int count, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        for (var i = 0; i < count; i++)
            await appended.WaitAsync(cts.Token);
    }
}

/// <summary>
/// Drives real <see cref="TrackAired"/> events through the REAL production booth-log write path —
/// <c>BoothLogWriter</c> (the <see cref="IBoothLogEventConsumer"/> that captures a track's
/// <see cref="PersonaPickDiagnostics"/> synchronously at publish time, SPEC F86.1) and
/// <c>BoothLogDrainService</c> (the background drain loop) — wired exactly the way
/// <c>PlayoutServiceCollectionExtensions</c>/<c>BoothLogServiceCollectionExtensions</c> wire them in
/// production, with only the repository seam (<see cref="IBoothLogAppender"/>) faked. Both concrete
/// types are internal to <c>GenWave.MediaLibrary</c> — reached here only through the public
/// <see cref="BoothLogServiceCollectionExtensions.AddBoothLog"/> DI surface and the public
/// <see cref="IBoothLogEventConsumer"/>/<see cref="IHostedService"/> seams, never a direct reference.
/// </summary>
file static class BoothLogWriteHarness
{
    public static async Task<FakeBoothLogAppender> DriveThroughAsync(params StationEvent[] events)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        services.AddBoothLog("Host=nowhere;Database=test", new ConfigurationBuilder().Build());

        var appender = new FakeBoothLogAppender();
        services.AddSingleton<IBoothLogAppender>(appender); // wins over AddBoothLog's own registration (last-registered-wins).

        await using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None);

        var sink = provider.GetRequiredService<IBoothLogEventConsumer>();
        foreach (var evt in events)
            sink.Publish(evt);

        await appender.WaitForCallsAsync(events.Length, TimeSpan.FromSeconds(5));

        foreach (var hostedService in hostedServices)
            await hostedService.StopAsync(CancellationToken.None);

        return appender;
    }
}

public static class FeatureBoothLogPickStamp
{
    const string Pending = "Pending T74 — GET /api/booth-log pick exposure; see docs/PLAN.md Phase V24";

    static TasteRule ArtistRule(string artist, double weight) =>
        new(new TastePredicate(artist, null, null), new TasteContext([], null, null), weight);

    static TasteRule GenreRule(string genre, double weight) =>
        new(new TastePredicate(null, genre, null), new TasteContext([], null, null), weight);

    static TrackAired StampedTrackAiring(PersonaPickDiagnostics? personaPick) =>
        new("42", "Night Drive", "The Waveforms", -2.5, DateTimeOffset.UtcNow, 214_000, personaPick);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPickStampedAtAirTime
    {
        /// <summary>
        /// Arrange + act shared by every fact below: a persona-ranked pick with two fired rules
        /// (artist +0.6, genre −0.3) and IsExploration=false flows through the playout event sink's
        /// track-start write; each fact below asserts one thing about the resulting stored jsonb.
        /// </summary>
        static async Task<string> DriveOnePickedTrackAsync()
        {
            var diagnostics = new PersonaPickDiagnostics(
                PoolSize: 40,
                TopScores: [0.91, 0.62, 0.58],
                FiredRules: [ArtistRule("The Weeknd", 0.6), GenreRule("Synthwave", -0.3)],
                IsExploration: false);

            var appender = await BoothLogWriteHarness.DriveThroughAsync(StampedTrackAiring(diagnostics));

            var pick = Assert.Single(appender.Calls).Pick;
            Assert.NotNull(pick); // sanity: a real pick was in fact stamped.
            return pick;
        }

        [Fact]
        public async Task StampCarriesEveryFiredRuleSummary()
        {
            var pick = await DriveOnePickedTrackAsync();

            using var document = JsonDocument.Parse(pick);
            var summaries = document.RootElement.GetProperty("firedRules").EnumerateArray()
                .Select(rule => rule.GetProperty("summary").GetString())
                .ToList();

            Assert.Equal(["The Weeknd", "Synthwave"], summaries);
        }

        [Fact]
        public async Task StampCarriesSignedWeights()
        {
            var pick = await DriveOnePickedTrackAsync();

            using var document = JsonDocument.Parse(pick);
            var weights = document.RootElement.GetProperty("firedRules").EnumerateArray()
                .Select(rule => rule.GetProperty("weight").GetDouble())
                .ToList();

            Assert.Equal([0.6, -0.3], weights);
        }

        [Fact]
        public async Task StampCarriesTheExplorationFlag()
        {
            var pick = await DriveOnePickedTrackAsync();

            using var document = JsonDocument.Parse(pick);

            Assert.False(document.RootElement.GetProperty("isExploration").GetBoolean());
        }

        [Fact]
        public async Task StampStoresNoScoresPoolSizeOrDegradationStep()
        {
            var pick = await DriveOnePickedTrackAsync();

            using var document = JsonDocument.Parse(pick);
            var topLevelProperties = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

            Assert.Equal(new HashSet<string> { "firedRules", "isExploration" }, topLevelProperties);
        }
    }

    public sealed class ScenarioApiExposesTheStamp
    {
        // Arrange (when built): a stamped track row exists; GET /api/booth-log is driven
        // through the production controller pipeline.

        [Fact(Skip = Pending)]
        public void StampedTrackRowCarriesPickWithRuleSummariesAndWeights()
        {
            // The entry's pick.firedRules mirrors the stored summaries + weights (F86.2).
        }

        [Fact(Skip = Pending)]
        public void StampedTrackRowCarriesTheExplorationFlag()
        {
            // The entry's pick.isExploration mirrors the stored flag (F86.2).
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — null stamps, no backfill, no leakage
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnstampedRowsStayNull
    {
        [Fact]
        public async Task EngineInitiatedPlayWritesANullPick()
        {
            // Given an engine-initiated play — the feeder never pushed this id, so its TrackAired
            // event carries no PersonaPick at all (SPEC F86.1)...
            var engineInitiated = new TrackAired(
                "77", "Static", "Nobody", -3.0, DateTimeOffset.UtcNow, DurationMs: null, PersonaPick: null);

            // When it flows through the playout event sink's track-start write...
            var appender = await BoothLogWriteHarness.DriveThroughAsync(engineInitiated);

            // Then the stored row's pick is null.
            Assert.Null(Assert.Single(appender.Calls).Pick);
        }

        [Fact]
        public async Task PersonaOffPickWritesANullPick()
        {
            // Given the persona layer disabled — Orchestrator never attaches a PersonaPickDiagnostics
            // to the playout-facing MediaItem, so the aired track's TrackAired event carries none
            // either (F83.3 posture)...
            var personaOffAiring = StampedTrackAiring(personaPick: null);

            // When it flows through the playout event sink's track-start write...
            var appender = await BoothLogWriteHarness.DriveThroughAsync(personaOffAiring);

            // Then the stored row's pick is null.
            Assert.Null(Assert.Single(appender.Calls).Pick);
        }

        [Fact]
        public async Task PreColumnRowsAreNeverBackfilled()
        {
            // Given an already-written row with no pick (the F84.6 precedent: predates the column,
            // an engine-initiated play, or a persona-off pick — all equally unstamped) followed by a
            // later, genuinely stamped pick for a DIFFERENT airing...
            var predatesTheColumn = new TrackAired(
                "1", "Old Song", "Old Artist", -2.0, DateTimeOffset.UtcNow, 180_000, PersonaPick: null);
            var laterDiagnostics = new PersonaPickDiagnostics(10, [0.5], [ArtistRule("Nova", 0.4)], IsExploration: false);
            var laterStamped = new TrackAired(
                "2", "New Song", "New Artist", -2.0, DateTimeOffset.UtcNow, 180_000, laterDiagnostics);

            // When both flow through the playout event sink, in order...
            var appender = await BoothLogWriteHarness.DriveThroughAsync(predatesTheColumn, laterStamped);

            // Then the earlier, unstamped row's pick stays null — the later write only ever produces
            // its OWN independent append call (F86.1); nothing here can retroactively fill in a row
            // that already exists.
            Assert.Null(appender.Calls[0].Pick);
        }

        [Fact(Skip = Pending)]
        public void NullPickRowsOmitThePickFieldFromTheApi()
        {
            // GET /api/booth-log entries for null-pick rows carry NO pick field —
            // absent, not null-valued (F86.2).
        }
    }

    public sealed class ScenarioExplorationExcludesRuleAttribution
    {
        [Fact]
        public async Task AnExplorationStampCarriesZeroFiredRules()
        {
            // Given an exploration pick — bias-blind by construction, so FiredRules is already empty
            // before it ever reaches the booth log (F82.4, F83.2)...
            var explorationDiagnostics = new PersonaPickDiagnostics(20, [0.3], FiredRules: [], IsExploration: true);

            // When it flows through the playout event sink's track-start write...
            var appender = await BoothLogWriteHarness.DriveThroughAsync(StampedTrackAiring(explorationDiagnostics));

            // Then the stored pick's firedRules array is empty — an exploration pick is never
            // attributed to a rule (F86.5).
            var pick = Assert.Single(appender.Calls).Pick;
            Assert.NotNull(pick);
            using var document = JsonDocument.Parse(pick);
            Assert.Empty(document.RootElement.GetProperty("firedRules").EnumerateArray());
        }
    }

    public sealed class ScenarioNoSpectatorLeakage
    {
        [Fact]
        public void SpectatorPayloadContractsCarryNoPickData()
        {
            // Given every public Spectator-prefixed DTO type in GenWave.Host.Api (the F62.9
            // disclosure-by-construction set)...
            var spectatorTypes = typeof(SpectatorController).Assembly.GetTypes()
                .Where(type => type.IsPublic
                    && type.Namespace == "GenWave.Host.Api"
                    && type.Name.StartsWith("Spectator", StringComparison.Ordinal));

            // When each type's public instance members are inspected for pick/firedRules/exploration
            // vocabulary...
            var forbidden = new[] { "pick", "firedrules", "isexploration", "exploration" };
            var offendingMembers = spectatorTypes
                .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => $"{type.Name}.{property.Name}"))
                .Where(name => forbidden.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Then none of them expose pick, firedRules, or an exploration flag (F86.9).
            Assert.Empty(offendingMembers);
        }
    }
}
