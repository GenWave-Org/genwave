// STORY-244 — Listeners see who's on and who's next (SPEC F93.1/F93.2/F93.4/F93.5, PLAN T125)
//
// BDD specification — xUnit. Entry-point discipline: every scenario drives the real
// GET /spectator/api/now-playing through WebApplicationFactory<Program>, credential-free,
// across staffed / music-only / gap / standby states seeded via the resolver's week snapshot.

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.Orchestration;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Real Program.cs composition root (mirrors <c>Story168_SpectatorNowPlaying.cs</c>/
/// <c>Story241_StatusPersonaResolverSourced.cs</c>'s own factories): hosted services and the media
/// catalog are swapped for controllable fakes so no Postgres/Liquidsoap connection is ever
/// attempted. <see cref="IScheduleStore"/> and <see cref="TimeProvider"/> are ALSO swapped — for a
/// controllable week grid and wall clock — while <see cref="CachingScheduleResolver"/>/
/// <see cref="ScheduleResolver"/> themselves stay the REAL production types resolving through the
/// real DI graph: this proves the actual resolver math (SPEC F91.2/F91.3), not a re-implementation
/// of it. <see cref="IActivePersonaAccessor"/> is replaced with the shared
/// <see cref="FakeActivePersonaAccessor"/> so a persona id's display name is scriptable without a
/// live orchestrator ever running to warm it.
/// </summary>
file sealed class WhoIsOnWebFactory(
    IScheduleStore scheduleStore, FakeTimeProvider timeProvider, FakeActivePersonaAccessor personaAccessor)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(personaAccessor);
            services.RemoveAll<IScheduleStore>();
            services.AddSingleton(scheduleStore);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(timeProvider);
        });
    }
}

public static class FeatureWhoIsOnAndWhoIsNext
{
    // Wednesday, UTC, so no DST/timezone concern rides these facts (mirrors ScheduleResolver's own
    // spec fixtures) — 1440 minutes past local midnight is the schema's own "runs to midnight" value.
    static readonly DateTimeOffset Now = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero); // Wed 10:00 UTC
    const int Midnight = 24 * 60;

    static readonly DateTimeOffset TrackStartedAt = Now.AddMinutes(-5);

    // DjName (gh-#259) is the airing item's own plan-time attribution stamp — the source of the
    // public dj field since attribution moved off the schedule's live answer onto the item itself.
    static NowPlayingSnapshot TrackSnapshot(string? djName = null) =>
        new(MediaId: "42", Title: "Night Drive", Artist: "The Waveforms", GainDb: -2.5,
            StartedAt: TrackStartedAt, DurationMs: 214_000, IsDrain: false, DjName: djName);

    static NowPlayingSnapshot PatterSnapshot(string? djName = null) =>
        new(MediaId: "tts:abc123", Title: "Generated patter text — operator content", Artist: null,
            GainDb: 0, StartedAt: TrackStartedAt, DurationMs: 12_345, IsDrain: false, DjName: djName);

    static WebApplicationFactory<Program> BuildFactory(
        IReadOnlyList<ScheduleSegment> segments, out FakeScheduleStore store, out FakeActivePersonaAccessor accessor,
        DateTimeOffset? now = null)
    {
        store = new FakeScheduleStore(new ScheduleWeekSnapshot(segments));
        accessor = new FakeActivePersonaAccessor();
        var clock = new FakeTimeProvider(now ?? Now);
        return new WhoIsOnWebFactory(store, clock, accessor);
    }

    /// <summary>
    /// Warms <see cref="CachingScheduleResolver"/>'s cached week snapshot exactly once — the test's
    /// stand-in for production's per-unit <c>Orchestrator</c>/<c>RankerPersonaPickProvider</c> resolve
    /// (see that type's own remarks). <see cref="CachingScheduleResolver.TryGetCurrent"/> answers null
    /// until this has run once; nothing on the poll path itself ever calls
    /// <see cref="IScheduleStore.LoadWeekAsync"/> (SPEC F93.4) — proved directly by
    /// <see cref="ScenarioHotPathStaysInMemory"/> below.
    /// </summary>
    static Task WarmScheduleAsync(IServiceProvider services) =>
        services.GetRequiredService<CachingScheduleResolver>().ResolveAsync(CancellationToken.None);

    static async Task<JsonElement> FetchNowPlayingAsync(WebApplicationFactory<Program> factory, NowPlayingSnapshot? snapshot)
    {
        await WarmScheduleAsync(factory.Services);
        if (snapshot is not null)
            factory.Services.GetRequiredService<NowPlayingService>().Update("1", snapshot); // SingleStation.IdString

        var client = factory.CreateClient();
        var response = await client.GetAsync("/spectator/api/now-playing");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioDjOnBothStates
    {
        // Given a staffed segment on air (F93.1).
        static ScheduleSegment StaffedAllDay(long personaId) =>
            new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: Midnight,
                PersonaId: personaId, Genres: null, EnergyMin: null, EnergyMax: null);

        [Fact]
        public async Task TrackStateCarriesTheOnAirDisplayName()
        {
            // gh-#259: the name comes from the airing item's own stamp, not the schedule row —
            // the staffed grid is still seeded to prove it isn't what supplies the value.
            await using var factory = BuildFactory([StaffedAllDay(1)], out _, out var accessor);
            accessor.Names[1] = "Nova";

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot(djName: "Nova"));

            Assert.Equal("Nova", body.GetProperty("dj").GetString());
        }

        [Fact]
        public async Task PatterStateCarriesTheSameDisplayName()
        {
            await using var factory = BuildFactory([StaffedAllDay(1)], out _, out var accessor);
            accessor.Names[1] = "Nova";

            var body = await FetchNowPlayingAsync(factory, PatterSnapshot(djName: "Nova"));

            Assert.Equal("Nova", body.GetProperty("dj").GetString());
        }
    }

    public sealed class ScenarioAttributionFollowsTheAiringItem
    {
        // gh-#259 — the drain window: the schedule has already flipped to the incoming persona, but
        // the engine queue is still draining the PREVIOUS show's rendered items. The displayed dj
        // must name the voice actually on air (the item's stamp), flipping only when the new
        // schedule's items themselves reach air.

        static ScheduleSegment[] EchoOnShiftNow() =>
        [
            new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: 600,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null), // 00:00–10:00 Nova — just ended
            new(Id: 2, Day: DayOfWeek.Wednesday, StartMinute: 600, EndMinute: Midnight,
                PersonaId: 2, Genres: null, EnergyMin: null, EnergyMax: null), // 10:00–24:00 Echo — on the grid NOW
        ];

        [Fact]
        public async Task AQueuedPatterOfThePreviousShowKeepsItsOwnDjWhileDraining()
        {
            await using var factory = BuildFactory(EchoOnShiftNow(), out _, out var accessor);
            accessor.Names[1] = "Nova";
            accessor.Names[2] = "Echo";

            // Nova's patter — planned before the boundary — is what is actually airing.
            var body = await FetchNowPlayingAsync(factory, PatterSnapshot(djName: "Nova"));

            Assert.Equal("Nova", body.GetProperty("dj").GetString());
        }

        [Fact]
        public async Task AnItemStampedWithNoDjReportsNullEvenOnAStaffedGrid()
        {
            // An engine-initiated play (safe rotation) or a pre-schedule leftover carries no stamp:
            // dj stays an honest null rather than borrowing the schedule's answer.
            await using var factory = BuildFactory(EchoOnShiftNow(), out _, out var accessor);
            accessor.Names[2] = "Echo";

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot(djName: null));

            Assert.Equal(JsonValueKind.Null, body.GetProperty("dj").ValueKind);
        }
    }

    public sealed class ScenarioExactlyOneUpNext
    {
        // Given a stored week with a future segment (F93.2): the on-air segment (persona A) is
        // immediately followed, with no gap, by a different persona's segment (B).
        static ScheduleSegment[] TwoStaffedSegments(long? nextPersonaId) =>
        [
            new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: 660,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null), // 00:00–11:00, persona 1
            new(Id: 2, Day: DayOfWeek.Wednesday, StartMinute: 660, EndMinute: Midnight,
                PersonaId: nextPersonaId, Genres: null, EnergyMin: null, EnergyMax: null), // 11:00–24:00
        ];

        [Fact]
        public async Task UpNextCarriesStartsAtAndDj()
        {
            await using var factory = BuildFactory(TwoStaffedSegments(nextPersonaId: 2), out _, out var accessor);
            accessor.Names[1] = "Nova";
            accessor.Names[2] = "Echo";

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            var upNext = body.GetProperty("upNext");
            var expectedBoundary = new DateTimeOffset(2026, 7, 29, 11, 0, 0, TimeSpan.Zero);
            Assert.Equal(expectedBoundary, upNext.GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal("Echo", upNext.GetProperty("dj").GetString());
        }

        [Fact]
        public async Task MusicOnlyNextCarriesNullDj()
        {
            await using var factory = BuildFactory(TwoStaffedSegments(nextPersonaId: null), out _, out var accessor);
            accessor.Names[1] = "Nova";

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            var upNext = body.GetProperty("upNext");
            Assert.Equal(JsonValueKind.Null, upNext.GetProperty("dj").ValueKind);
        }

        [Fact]
        public async Task NoDeeperLookaheadExistsInAnyPublicPayload()
        {
            // upNext itself carries exactly {startsAt, dj} — no nested "next", no further segments,
            // no schedule/week structure of any kind (F93.2's "no deeper lookahead").
            await using var factory = BuildFactory(TwoStaffedSegments(nextPersonaId: 2), out _, out var accessor);
            accessor.Names[1] = "Nova";
            accessor.Names[2] = "Echo";

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            var upNextProperties = body.GetProperty("upNext").EnumerateObject()
                .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(new HashSet<string>(["startsAt", "dj"]), upNextProperties);
        }
    }

    public sealed class ScenarioUpNextCollapseArms
    {
        // PLAN T125 review F3 — pins every arm of SpectatorUpNext's same-persona collapse rule (see
        // that type's own remarks): a single comparison in ResolveUpNext handles all of them, but
        // each arm was reachable without a dedicated fact proving it before this class.

        static ScheduleSegment[] TwoStaffedSegments(long? nextPersonaId) =>
        [
            new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: 660,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null), // 00:00–11:00, persona 1
            new(Id: 2, Day: DayOfWeek.Wednesday, StartMinute: 660, EndMinute: Midnight,
                PersonaId: nextPersonaId, Genres: null, EnergyMin: null, EnergyMax: null), // 11:00–24:00
        ];

        [Fact]
        public async Task SamePersonaAdjacentRowsCollapseUpNextToNull()
        {
            // F92.3's same-persona ruling extended to this public surface: the resolver still
            // reports NextSegment/BoundaryAt (row-accurate), but the SAME persona on both sides of
            // the boundary is nothing a listener needs announced — collapse the WHOLE upNext
            // property to null, not merely its dj.
            await using var factory = BuildFactory(TwoStaffedSegments(nextPersonaId: 1), out _, out var accessor);
            accessor.Names[1] = "Nova";

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            Assert.Equal(JsonValueKind.Null, body.GetProperty("upNext").ValueKind);
        }

        [Fact]
        public async Task StaffedSegmentFollowedByAGapCarriesNullDjUpNext()
        {
            // A TRUE grid gap follows (no row at all — NextSegment itself is null), distinct from
            // MusicOnlyNextCarriesNullDj's explicit music-only row above — same "dj: null" outcome
            // via the SAME single comparison (ResolveUpNext never special-cases the two shapes).
            var staffedThenNothingElseScheduled = new ScheduleSegment(
                Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: 660,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null); // 00:00–11:00 only
            await using var factory = BuildFactory([staffedThenNothingElseScheduled], out _, out var accessor);
            accessor.Names[1] = "Nova";

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            var upNext = body.GetProperty("upNext");
            var expectedBoundary = new DateTimeOffset(2026, 7, 29, 11, 0, 0, TimeSpan.Zero);
            Assert.Equal(expectedBoundary, upNext.GetProperty("startsAt").GetDateTimeOffset());
            Assert.Equal(JsonValueKind.Null, upNext.GetProperty("dj").ValueKind);
        }

        [Fact]
        public async Task MusicOnlyFollowedByMusicOnlyCollapsesUpNextToNull()
        {
            // Already IMPLIED by the same null-equals-null comparison (F3: "include if cheap") —
            // pinned explicitly so a future refactor of ResolveUpNext can't silently special-case it.
            ScheduleSegment[] musicOnlyThenMusicOnly =
            [
                new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: 660,
                    PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null), // 00:00–11:00
                new(Id: 2, Day: DayOfWeek.Wednesday, StartMinute: 660, EndMinute: Midnight,
                    PersonaId: null, Genres: null, EnergyMin: null, EnergyMax: null), // 11:00–24:00
            ];
            await using var factory = BuildFactory(musicOnlyThenMusicOnly, out _, out _);

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            Assert.Equal(JsonValueKind.Null, body.GetProperty("upNext").ValueKind);
        }
    }

    public sealed class ScenarioHotPathStaysInMemory
    {
        // Given the poll path under load (F93.4).
        static ScheduleSegment[] StaffedAllDay => [
            new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: Midnight,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null),
        ];

        [Fact]
        public async Task AssemblyIssuesNoDbOrEngineCall()
        {
            await using var factory = BuildFactory(StaffedAllDay, out var store, out var accessor);
            accessor.Names[1] = "Nova";
            await WarmScheduleAsync(factory.Services); // the one and only load — mirrors production's per-unit resolve
            factory.Services.GetRequiredService<NowPlayingService>().Update("1", TrackSnapshot());
            var client = factory.CreateClient();

            for (var i = 0; i < 5; i++)
                await client.GetAsync("/spectator/api/now-playing");

            Assert.Equal(1, store.LoadWeekAsyncCallCount);
        }

        [Fact]
        public async Task ExistingCachePoliciesAndLimitsAreUnchanged()
        {
            await using var factory = BuildFactory(StaffedAllDay, out _, out var accessor);
            accessor.Names[1] = "Nova";
            await WarmScheduleAsync(factory.Services);
            factory.Services.GetRequiredService<NowPlayingService>().Update("1", TrackSnapshot());
            var client = factory.CreateClient();

            var response = await client.GetAsync("/spectator/api/now-playing");

            var cache = response.Headers.CacheControl;
            Assert.True(cache is { Public: true, MaxAge: not null } && (int)cache.MaxAge.Value.TotalSeconds == 5,
                $"Cache-Control was '{cache}' — expected public, max-age=5 (SPEC F62.10, unchanged by T125).");
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioUnstaffedAndStandbyAreHonest
    {
        // Sad path — music-only segment, grid gap, standby (F93.1, F93.5).

        [Fact]
        public async Task MusicOnlyAndGapReturnNullDj()
        {
            // Music-only: a real schedule row, but PersonaId null.
            var musicOnly = BuildFactory(
                [new ScheduleSegment(1, DayOfWeek.Wednesday, 0, Midnight, PersonaId: null, null, null, null)],
                out _, out _);
            await using (musicOnly)
            {
                var body = await FetchNowPlayingAsync(musicOnly, TrackSnapshot());
                Assert.Equal(JsonValueKind.Null, body.GetProperty("dj").ValueKind);
            }

            // Grid gap: no schedule rows at all.
            var gap = BuildFactory([], out _, out _);
            await using (gap)
            {
                var body = await FetchNowPlayingAsync(gap, TrackSnapshot());
                Assert.Equal(JsonValueKind.Null, body.GetProperty("dj").ValueKind);
            }
        }

        [Fact]
        public async Task StandbyShapeIsUnchanged()
        {
            await using var factory = BuildFactory([], out _, out _);

            var body = await FetchNowPlayingAsync(factory, snapshot: null); // feeder never ticked

            var properties = body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(new HashSet<string>(["listeners", "state"]), properties);
        }

        [Fact]
        public async Task DisclosureContractGainsExactlyDjUpNextArtworkUrl()
        {
            // Live-wire exhaustive shape for the track state — SPEC F93.5's own inventory, proved
            // over HTTP rather than only against the DTO in isolation (Story183 owns that half).
            await using var factory = BuildFactory(
                [new ScheduleSegment(1, DayOfWeek.Wednesday, 0, Midnight, PersonaId: 1, null, null, null)],
                out _, out var accessor);
            accessor.Names[1] = "Nova";

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            var properties = body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(
                new HashSet<string>(
                    ["title", "artist", "startedAt", "durationMs", "listeners", "dj", "upNext", "artworkUrl", "state", "kind"]),
                properties);
        }
    }
}
