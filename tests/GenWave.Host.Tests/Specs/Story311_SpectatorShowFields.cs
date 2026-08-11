// STORY-311 — The public face names the show (F116.4, F115.3)
//
// BDD specification — xUnit. Entry-point discipline: the happy-path scenario drives the real
// GET /spectator/api/now-playing through WebApplicationFactory<Program> (mirrors Story244's own
// WhoIsOnWebFactory) — credential-free, staffed/unstaffed states seeded via the resolver's week
// snapshot. Disclosure follows the F67.6 idiom — complete-property-set assertions so an unblessed
// field fails the build. The one hard law: flavor is prompt config and NEVER appears on a public
// surface (F115.3) — proven here structurally, by reflecting over every public type this
// assembly's spectator surface owns, the same idiom Story221's F86.9 gate uses for taste/mood.

using System.Reflection;
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
using GenWave.Host.Api;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Fakes;
using GenWave.Orchestration;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Real Program.cs composition root (mirrors Story244's own <c>WhoIsOnWebFactory</c>): hosted
/// services and the media catalog are swapped for controllable fakes so no Postgres/Liquidsoap
/// connection is ever attempted. <see cref="IScheduleStore"/> and <see cref="TimeProvider"/> are
/// ALSO swapped — for a controllable week grid and wall clock — while
/// <see cref="CachingScheduleResolver"/>/<see cref="ScheduleResolver"/> themselves stay the REAL
/// production types resolving through the real DI graph, so <c>show</c>/<c>upNext.show</c> prove
/// the actual resolver + <c>EffectiveAssignment</c> chokepoint (SPEC F115.2), not a
/// re-implementation of it.
/// </summary>
file sealed class ShowFieldsWebFactory(IScheduleStore scheduleStore, FakeTimeProvider timeProvider)
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
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.RemoveAll<IScheduleStore>();
            services.AddSingleton(scheduleStore);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(timeProvider);
        });
    }
}

public static class FeatureSpectatorShowFields
{
    // Wednesday, UTC — mirrors Story244's own fixture, no DST/timezone concern rides these facts.
    static readonly DateTimeOffset Now = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero); // Wed 10:00 UTC
    const int Midnight = 24 * 60;

    static readonly DateTimeOffset TrackStartedAt = Now.AddMinutes(-5);

    // Flavor strings are DELIBERATELY DISTINCT per show (never the same literal) — each fact that
    // fetches a live payload with one of these on air asserts its own raw body never contains ITS
    // flavor text, the value-level guard the reflection sweep in ScenarioDisclosureHoldsTheLine
    // cannot itself provide (that sweep is name-level only — see its own remarks).
    static readonly ShowSummary NightDriveRadio =
        new(Id: 1, Name: "Night Drive Radio", Tagline: "Two hours of driving synths",
            Flavor: "moody synthwave, late-night driving energy — prompt only, never public");

    static readonly ShowSummary EchoChamber =
        new(Id: 2, Name: "Echo Chamber", Tagline: null,
            Flavor: "ambient soundscapes, reverb-drenched — prompt only, never public");

    static NowPlayingSnapshot TrackSnapshot() =>
        new(MediaId: "42", Title: "Night Drive", Artist: "The Waveforms", GainDb: -2.5,
            StartedAt: TrackStartedAt, DurationMs: 214_000, IsDrain: false);

    static WebApplicationFactory<Program> BuildFactory(
        IReadOnlyList<ScheduleSegment> segments, DateTimeOffset? now = null)
    {
        var store = new FakeScheduleStore(new ScheduleWeekSnapshot(segments));
        var clock = new FakeTimeProvider(now ?? Now);
        return new ShowFieldsWebFactory(store, clock);
    }

    /// <summary>Warms <see cref="CachingScheduleResolver"/>'s cached week snapshot exactly once —
    /// mirrors Story244's own <c>WarmScheduleAsync</c>. <see cref="CachingScheduleResolver.TryGetCurrent"/>
    /// answers null until this has run once.</summary>
    static Task WarmScheduleAsync(IServiceProvider services) =>
        services.GetRequiredService<CachingScheduleResolver>().ResolveAsync(CancellationToken.None);

    static async Task<JsonElement> FetchNowPlayingAsync(WebApplicationFactory<Program> factory, NowPlayingSnapshot snapshot)
    {
        await WarmScheduleAsync(factory.Services);
        factory.Services.GetRequiredService<NowPlayingService>().Update("1", snapshot); // SingleStation.IdString

        var client = factory.CreateClient();
        var response = await client.GetAsync("/spectator/api/now-playing");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    public sealed class ScenarioTheFieldsRide
    {
        // Given a named show on air, immediately followed (no gap) by a different, also-named show
        // (F116.4) — two persona ids so the same-persona-and-same-show upNext collapse never masks
        // the assertion. ShowId is set to match each Show's own Id on every segment below — the
        // real load path (GenWave.MediaLibrary.Station.ScheduleRepository) guarantees the two agree
        // for a loaded segment; leaving ShowId null while Show is set would silently satisfy
        // ResolveUpNext's collapse guard on the WRONG field and prove nothing (PLAN T251 review F1).
        static ScheduleSegment[] NightDriveThenEchoChamber() =>
        [
            new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: 660,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null,
                Show: NightDriveRadio, ShowId: NightDriveRadio.Id), // 00:00–11:00
            new(Id: 2, Day: DayOfWeek.Wednesday, StartMinute: 660, EndMinute: Midnight,
                PersonaId: 2, Genres: null, EnergyMin: null, EnergyMax: null,
                Show: EchoChamber, ShowId: EchoChamber.Id), // 11:00–24:00
        ];

        [Fact]
        public async Task NowPlayingCarriesShowNameAndTagline()
        {
            await using var factory = BuildFactory(NightDriveThenEchoChamber());

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            var show = body.GetProperty("show");
            Assert.Equal("Night Drive Radio", show.GetProperty("name").GetString());
            Assert.Equal("Two hours of driving synths", show.GetProperty("tagline").GetString());

            // Value-level guard (PLAN T251 review): the on-air show's own Flavor text never rides
            // the wire, regardless of property name — covers a mis-wire the name-only reflection
            // sweep (ScenarioDisclosureHoldsTheLine) structurally cannot.
            var flavor = NightDriveRadio.Flavor;
            Assert.NotNull(flavor);
            Assert.DoesNotContain(flavor, body.GetRawText(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task UpNextCarriesTheShowName()
        {
            await using var factory = BuildFactory(NightDriveThenEchoChamber());

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            var upNextShow = body.GetProperty("upNext").GetProperty("show");
            Assert.Equal("Echo Chamber", upNextShow.GetProperty("name").GetString());

            // Value-level guard (PLAN T251 review) — same discipline as the sibling fact above,
            // for the UPCOMING show's own distinct Flavor text.
            var echoChamberFlavor = EchoChamber.Flavor;
            Assert.NotNull(echoChamberFlavor);
            Assert.DoesNotContain(echoChamberFlavor, body.GetRawText(), StringComparison.Ordinal);

            // NAME ONLY (F116.4) — no tagline property at all, even though EchoChamber carries none
            // and NightDriveRadio (the OTHER show, proving this isn't a null-value coincidence)
            // does. Pinned again here, alongside Story183/230/248's own copies, per this suite's
            // "every gate owns its pin" convention.
            var upNextShowProperties = upNextShow.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(new HashSet<string>(["name"]), upNextShowProperties);
        }

        [Fact]
        public async Task UnnamedBlocksReadNull()
        {
            // Given no show on the air and an unnamed next segment — different persona ids so
            // upNext itself still reports (rather than collapsing under the same-persona rule),
            // proving show reads null on its own merits, not because upNext vanished entirely.
            ScheduleSegment[] unnamedThenUnnamed =
            [
                new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: 660,
                    PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null), // Show left null
                new(Id: 2, Day: DayOfWeek.Wednesday, StartMinute: 660, EndMinute: Midnight,
                    PersonaId: 2, Genres: null, EnergyMin: null, EnergyMax: null), // Show left null
            ];
            await using var factory = BuildFactory(unnamedThenUnnamed);

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            Assert.Equal(JsonValueKind.Null, body.GetProperty("show").ValueKind);
            var upNext = body.GetProperty("upNext");
            Assert.NotEqual(JsonValueKind.Null, upNext.ValueKind); // still reports — different personas
            Assert.Equal(JsonValueKind.Null, upNext.GetProperty("show").ValueKind);
        }
    }

    public sealed class ScenarioSamePersonaDifferentShowStillAnnounces
    {
        // SPEC F116.2 (ruled at PLAN T251 review, F1): a same-persona DIFFERENT-show boundary airs
        // a real ceremony piece on air — the F91.6 demo's single-DJ seed makes EVERY boundary shape
        // this way, so ResolveUpNext's collapse guard must key on persona AND show, not persona
        // alone, or upNext silently disagrees with what listeners actually hear. ShowId (not the
        // Show record's own Id) is what the guard itself compares — set here to match Show.Id, as
        // the real load path always does.
        static ScheduleSegment[] SamePersonaDifferentShow() =>
        [
            new(Id: 1, Day: DayOfWeek.Wednesday, StartMinute: 0, EndMinute: 660,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null,
                Show: NightDriveRadio, ShowId: NightDriveRadio.Id), // 00:00–11:00 — persona 1
            new(Id: 2, Day: DayOfWeek.Wednesday, StartMinute: 660, EndMinute: Midnight,
                PersonaId: 1, Genres: null, EnergyMin: null, EnergyMax: null,
                Show: EchoChamber, ShowId: EchoChamber.Id), // 11:00–24:00 — SAME persona 1, different show
        ];

        [Fact]
        public async Task SamePersonaDifferentShowUpNextNamesTheIncomingShow()
        {
            await using var factory = BuildFactory(SamePersonaDifferentShow());

            var body = await FetchNowPlayingAsync(factory, TrackSnapshot());

            // upNext must NOT collapse to null (a persona-only guard would wrongly do so here) —
            // it names the incoming show exactly as the on-air ceremony does (F116.2).
            var upNext = body.GetProperty("upNext");
            Assert.NotEqual(JsonValueKind.Null, upNext.ValueKind);
            Assert.Equal("Echo Chamber", upNext.GetProperty("show").GetProperty("name").GetString());
        }
    }

    public sealed class ScenarioDisclosureHoldsTheLine
    {
        [Fact]
        public void FlavorIsStructurallyAbsentFromPublicPayloads()
        {
            // Given every public spectator-facing wire DTO (the Story183/230/248 "Spectator*"
            // by-prefix census — deliberately narrower than the whole GenWave.Host.Api namespace:
            // the ADMIN Shows editor's ShowDto/ShowRequest legitimately carry Flavor for CRUD,
            // F115.3 forbids it on the PUBLIC surface only)...
            var spectatorTypes = typeof(SpectatorController).Assembly.GetTypes()
                .Where(type => type.IsPublic
                    && type.Namespace == "GenWave.Host.Api"
                    && type.Name.StartsWith("Spectator", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(spectatorTypes);

            // When every public instance property name is inspected for "flavor" vocabulary...
            var offendingMembers = spectatorTypes
                .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => $"{type.Name}.{property.Name}"))
                .Where(name => name.Contains("flavor", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Then none of them expose it — not SpectatorShow, not SpectatorUpNextShow, not any
            // other public spectator type (F115.3, the persona-soul precedent). This guarantee is
            // NAME-LEVEL and top-level only (no public Spectator-prefixed type has a "flavor"-named
            // member) — it cannot catch a value smuggled through a differently-named property. The
            // VALUE-level guard covering that mis-wire case lives in ScenarioTheFieldsRide's own
            // facts above: NightDriveRadio/EchoChamber carry distinct, non-null Flavor strings, and
            // each fact asserts ITS show's own string is absent from the fetched payload's raw text.
            Assert.Empty(offendingMembers);
        }
    }
}
