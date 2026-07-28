// STORY-240–249 — Acceptance gate: the format clock converges (Epic FC / SPEC F91-F95, PLAN T131).
//
// BDD specification — xUnit. T118-T130 (this epic) replaced the single Station:Persona:ActiveId
// switch with the weekly format-clock grid: the seed-and-delete migration (T118), the pure
// ScheduleResolver (T119), the two re-backed seams (T120), the persona delete FK-guard (T121), the
// schedule week API (T122), sign-off/sign-on handoffs (T123/T124), the spectator dj/upNext/
// artworkUrl fields (T125/T126), and the Roster/Fire/editor/Hire admin-ui passes (T127-T130). This
// gate re-affirms the epic's disclosure, zero-diff, upgrade, and route-table promises independently
// of any single task's own specs — the Story141/147/153/162/212/221/230 idiom: every fact below is
// a real, always-run, non-Skip assertion, entirely in-process (WebApplicationFactory/static
// analysis, no docker dependency) — Story230's own lesson (a live-stack-gated fact flakes the gate
// itself) applies with extra force here, since nothing in this epic needs a live Kokoro/Icecast/
// Liquidsoap connection to prove.
//
// PLAN T131's own dependency line ("depends-on: ... T124 (or its ruled drop)") is moot by the time
// this gate builds: T124 shipped (03914d6..fd83126's own history carries it), so this gate holds
// the FULL epic, not a droppable-handoffs shrunk one — no PLAN.md edit needed, just this note.

using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Api;
using GenWave.Orchestration;

namespace GenWave.Host.Tests.Specs;

/// <summary>Stamps every request's Connection.LocalPort so the SurfaceGate sees the simulated
/// listener (mirrors Story172/238's own SimulatedPortStartupFilter — file-scoped, so this file
/// needs its own copy).</summary>
file sealed class FormatClockGateStartupFilter(int port) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.LocalPort = port;
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>
/// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's route-table facts
/// (mirrors Story221's own <c>PersonaVisibilityGateWebFactory</c>) — only <see cref="EndpointDataSource"/>
/// metadata is ever inspected and, for the public-listener facts, a simulated
/// <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.LocalPort"/>; no request ever reaches a
/// controller action, so no <c>IScheduleStore</c>/<c>IMediaExplicitOverride</c> double is needed —
/// only the hosted-service/catalog/persona-accessor removals every gate factory in this suite shares.
/// </summary>
file sealed class FormatClockGateWebFactory(int? simulatedPublicPort = null) : WebApplicationFactory<Program>
{
    internal const int PublicPort = 8081;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        if (simulatedPublicPort is int port)
        {
            builder.UseSetting("Station:SpectatorMode", "true");
            builder.UseSetting("Spectator:PublicPort", port.ToString());
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            if (simulatedPublicPort is int port)
                services.AddSingleton<IStartupFilter>(new FormatClockGateStartupFilter(port));
        });
    }
}

/// <summary>Fixed <see cref="IStationDefaultEnvelopeSource"/> double — envelope content is
/// irrelevant to this gate's persona-resolution fact; only <see cref="OnAirSnapshot.PersonaId"/> is
/// ever asserted on.</summary>
file sealed class FixedEnvelopeSource : IStationDefaultEnvelopeSource
{
    public SegmentEnvelope Current => SegmentEnvelope.StationDefault;
}

public static class FeatureFormatClockGate
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107/
    /// 141/147/153/162/212/221/230's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Sha256Hex(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Every public Spectator-prefixed wire DTO in <c>GenWave.Host.Api</c> — the same
    /// by-prefix reflection discovery Story221/230 use, narrowed to actual serialized payload shapes
    /// by excluding controllers and marker attributes structurally (Story230's own idiom), so a
    /// brand-new controller/attribute never pollutes the census but a brand-new DTO (or field)
    /// always does.</summary>
    static IReadOnlyList<Type> SpectatorWireDtoTypes() =>
        typeof(SpectatorController).Assembly.GetTypes()
            .Where(type => type.IsPublic
                && type.Namespace == "GenWave.Host.Api"
                && type.Name.StartsWith("Spectator", StringComparison.Ordinal)
                && !typeof(ControllerBase).IsAssignableFrom(type)
                && !typeof(Attribute).IsAssignableFrom(type))
            .ToList();

    // ---------------------------------------------------------------------
    // PART 1 (F93.1-F93.5) — disclosure: exactly dj/upNext(startsAt,dj)/artworkUrl joined the
    // public surface this epic, nothing else.
    // ---------------------------------------------------------------------

    public static class ScenarioSpectatorDisclosureGainedExactlyThreeFields
    {
        /// <summary>
        /// The complete, ordinal-sorted "TypeName.PropertyName" census over every spectator wire DTO
        /// (Story230's own idiom and, as of that gate's own PLAN T125 amendment, already the
        /// post-epic shape) — this gate's OWN copy, independent of Story230's, per the house
        /// convention that every gate owns its pin rather than sharing one point of failure. Confirmed
        /// at this task's own build time to be byte-identical to Story230's PinnedFieldCensus: nothing
        /// on this surface moved since T125/T126 shipped — no drift found writing this gate.
        /// </summary>
        static readonly string[] PinnedFieldCensus =
        [
            "SpectatorAbout.License",
            "SpectatorAbout.ProjectUrl",
            "SpectatorAbout.RequestsEnabled",
            "SpectatorAbout.StationName",
            "SpectatorAbout.StreamUrl",
            "SpectatorAbout.Version",
            "SpectatorPatterNowPlaying.Dj",
            "SpectatorPatterNowPlaying.DurationMs",
            "SpectatorPatterNowPlaying.Kind",
            "SpectatorPatterNowPlaying.Listeners",
            "SpectatorPatterNowPlaying.StartedAt",
            "SpectatorPatterNowPlaying.State",
            "SpectatorPatterNowPlaying.UpNext",
            "SpectatorPlayHistoryPatterEntry.AiredAt",
            "SpectatorPlayHistoryPatterEntry.Kind",
            "SpectatorPlayHistoryResponse.Entries",
            "SpectatorPlayHistoryTrackEntry.AiredAt",
            "SpectatorPlayHistoryTrackEntry.Artist",
            "SpectatorPlayHistoryTrackEntry.Kind",
            "SpectatorPlayHistoryTrackEntry.Title",
            "SpectatorRequestAccepted.Note",
            "SpectatorRequestAccepted.Status",
            "SpectatorRequestSubmission.Wish",
            "SpectatorStandbyNowPlaying.Listeners",
            "SpectatorStandbyNowPlaying.State",
            "SpectatorStats.Enriching",
            "SpectatorStats.Failed",
            "SpectatorStats.Ready",
            "SpectatorTrackNowPlaying.Artist",
            "SpectatorTrackNowPlaying.ArtworkUrl",
            "SpectatorTrackNowPlaying.Dj",
            "SpectatorTrackNowPlaying.DurationMs",
            "SpectatorTrackNowPlaying.Kind",
            "SpectatorTrackNowPlaying.Listeners",
            "SpectatorTrackNowPlaying.StartedAt",
            "SpectatorTrackNowPlaying.State",
            "SpectatorTrackNowPlaying.Title",
            "SpectatorTrackNowPlaying.UpNext",
            "SpectatorUpNext.Dj",
            "SpectatorUpNext.StartsAt",
        ];

        [Fact]
        public static void TheFullSpectatorWireCensusIsExactlyThePinnedSet()
        {
            var actual = SpectatorWireDtoTypes()
                .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => $"{type.Name}.{property.Name}"))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(PinnedFieldCensus, actual);
        }

        [Fact]
        public static void NoSpectatorPayloadCarriesScheduleSegmentEnvelopeOrPersonaIdVocabulary()
        {
            var spectatorTypes = SpectatorWireDtoTypes();
            Assert.NotEmpty(spectatorTypes);

            var forbidden = new[] { "schedule", "segment", "envelope", "personaid", "boundary" };
            var offendingMembers = spectatorTypes
                .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => $"{type.Name}.{property.Name}"))
                .Where(name => forbidden.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            Assert.Empty(offendingMembers);
        }
    }

    // ---------------------------------------------------------------------
    // PART 2 — zero-diff engine/compose re-pin (Story141/147/153/162/212/221/230's own convention)
    // ---------------------------------------------------------------------

    public static class ScenarioEngineAndComposeCarryZeroDiffFromMain
    {
        // v2.7.0 epoch (T93's F88.4 export fix) — the SAME two hashes every other gate in this suite
        // carries (Story153/212/221/230's own copies). Verified at this task's own build time:
        // `git diff origin/main...HEAD -- engine/ compose.yaml` is empty, and a direct sha256sum of
        // both files matches these constants byte-for-byte — T118-T130 (the grid migration, the
        // resolver, the two re-backed seams, the FK-guard, the schedule API, the handoff producer,
        // the spectator wiring, the Roster/Fire/editor/Hire admin-ui passes) never touched either
        // file. No re-pin needed; this is that confirmation, run as its own always-green fact rather
        // than only asserted in a comment.
        const string EngineScriptSha256 = "11c8b3b59b4b641dc59fa4217e935442573adf04f8e756934e23593b17677049";
        const string ComposeYamlSha256  = "9ddd169329ef5b092638d1e67279272fc4d7b9f350dcc330cb455d7d92faf981";

        [Fact]
        public static void EngineScriptByteMatchesMain()
        {
            Assert.Equal(EngineScriptSha256, Sha256Hex(Path.Combine("engine", "genwave.liq")));
        }

        [Fact]
        public static void ComposeYamlByteMatchesMain()
        {
            Assert.Equal(ComposeYamlSha256, Sha256Hex("compose.yaml"));
        }
    }

    // ---------------------------------------------------------------------
    // PART 3 (F91.6) — upgrade-inaudible: the resolver-side half of "sounds identical"
    // ---------------------------------------------------------------------

    public static class ScenarioUpgradeIsInaudible
    {
        // db/27's seed-and-delete migration itself is proven directly against a real Postgres
        // instance in GenWave.MediaLibrary.Tests/Specs/Story242_UpgradeChangesNothing.cs
        // (Category=Integration — needs docker; deliberately NOT re-run here, per this gate's own
        // no-live-stack rule). That suite's ScenarioSeedingFromActiveId proves the migration produces
        // EXACTLY seven all-day rows (day 0-6, minute 0-1440), all seven naming the SAME migrated
        // persona id, envelope fields left null. What it does NOT itself prove — it never touches
        // ScheduleResolver at all — is the OTHER half of "sounds identical": that shape, fed through
        // the REAL resolver, actually resolves to that same persona at every instant of the week, the
        // way a pre-migration Station:Persona:ActiveId read always did (unconditionally, at any
        // wall-clock time). That is a pure, in-process fact — no DB, no docker — proven here, closing
        // the loop between "the migration produces this shape" (Story242) and "this shape sounds
        // identical to the old switch" (this gate) without duplicating either half.

        static ScheduleWeekSnapshot SeededMigrationShape(long migratedPersonaId) =>
            new(Enumerable.Range(0, 7)
                .Select(day => new ScheduleSegment(
                    Id: day + 1, Day: (DayOfWeek)day, StartMinute: 0, EndMinute: 1440,
                    PersonaId: migratedPersonaId, Genres: null, EnergyMin: null, EnergyMax: null))
                .ToList());

        /// <summary>A real Sunday (so <c>AddDays(0..6)</c> lands on <see cref="DayOfWeek.Sunday"/>..
        /// <see cref="DayOfWeek.Saturday"/> in order, matching the seeded shape's own
        /// <c>(DayOfWeek)day</c> construction) at four times of day per day — 28 samples spanning the
        /// whole week.</summary>
        static IEnumerable<DateTimeOffset> SampledInstantsAcrossTheWeek()
        {
            var sunday = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
            foreach (var day in Enumerable.Range(0, 7))
                foreach (var minutesPastMidnight in new[] { 0, 375, 750, 1439 })
                    yield return sunday.AddDays(day).AddMinutes(minutesPastMidnight);
        }

        [Fact]
        public static void EveryInstantOfTheWeekResolvesToTheSameMigratedPersona()
        {
            const long migratedPersonaId = 7;
            var snapshot = SeededMigrationShape(migratedPersonaId);
            var envelopeSource = new FixedEnvelopeSource();

            Assert.All(SampledInstantsAcrossTheWeek(), instant =>
            {
                var resolver = new ScheduleResolver(new FakeTimeProvider(instant), envelopeSource);

                var onAir = resolver.Resolve(snapshot);

                Assert.Equal(migratedPersonaId, onAir.PersonaId);
            });
        }
    }

    // ---------------------------------------------------------------------
    // PART 4 — the epic's new admin routes carry policies + AdminSurface, and stay off the public
    // listener
    // ---------------------------------------------------------------------

    public static class ScenarioNewAdminRoutesCarryPoliciesAndStayOffThePublicListener
    {
        /// <summary>
        /// (verb, route pattern raw text, expected named policy) for the schedule week API (T122,
        /// SPEC F91.8) and the explicit-classification override (T115, SPEC F95.3) — named
        /// explicitly, rather than left to Story163's blanket "has SOME policy" sweep, so a future
        /// regression that quietly loosens one of these to a lesser admin plane names the offending
        /// route rather than only failing a generic assertion.
        /// </summary>
        static readonly (string Verb, string Route, string ExpectedPolicy)[] NewAdminRoutes =
        [
            ("GET", "api/schedule", AuthorizationPolicies.Settings),
            ("PUT", "api/schedule", AuthorizationPolicies.Settings),
            ("PUT", "api/media/{id:long}/explicit", AuthorizationPolicies.Curation),
        ];

        [Fact]
        public static void EachNewRouteCarriesAdminSurfaceAndItsNamedPolicy()
        {
            using var factory = new FormatClockGateWebFactory();
            _ = factory.CreateClient(); // force host build

            var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .ToList();

            Assert.All(NewAdminRoutes, route =>
            {
                var endpoint = endpoints.SingleOrDefault(candidate =>
                        string.Equals(candidate.RoutePattern.RawText, route.Route, StringComparison.Ordinal)
                        && (candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(route.Verb) ?? false))
                    ?? throw new InvalidOperationException($"no endpoint found for {route.Verb} {route.Route}");

                Assert.NotNull(endpoint.Metadata.GetMetadata<AdminSurfaceAttribute>());

                var actualPolicy = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Select(authorizeData => authorizeData.Policy)
                    .SingleOrDefault(policy => !string.IsNullOrEmpty(policy));
                Assert.Equal(route.ExpectedPolicy, actualPolicy);
            });
        }

        [Theory]
        [InlineData("GET", "/api/schedule")]
        [InlineData("PUT", "/api/schedule")]
        [InlineData("PUT", "/api/media/1/explicit")]
        public static async Task RouteReturns404OnThePublicListener(string verb, string path)
        {
            await using var factory = new FormatClockGateWebFactory(FormatClockGateWebFactory.PublicPort);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(verb), path));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
