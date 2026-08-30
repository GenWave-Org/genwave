// STORY-380 — The knobs and the laws (SPEC F155 · PLAN T357, T366)
//
// BDD specification — xUnit. AC1/AC6 (T357) drive the REAL production binary
// (WebApplicationFactory<Program>, the Story084/Story125 "fake ConnectionStrings:Library, real
// composition root" idiom — no live Postgres needed: GardenerOptions is bound and boot-validated
// purely off IConfiguration, with no repository/table read on the path either AC exercises) reading
// GardenerOptions off the booted host's DI container. AC2/AC5 (T366) drive Station:Thumbs:Enabled
// through the real PUT /api/settings surface, which DOES need the ephemeral station+library Postgres
// (tests/GenWave.Host.Tests/Support/EphemeralStationDatabase) — see ThumbsLiveSwitchArc at the bottom
// of this file (the Story366/369 "one ephemeral db, one Arc, Facts just assert" shape).
// AC3 (the L5 reserved-namespace pin) and AC4 (the three-way disjointness pin) live in
// GenWave.Architecture.Tests, written by another agent — not this file.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using GenWave.Core.Abstractions;
using GenWave.Core.Events;
using GenWave.Host.Api;
using GenWave.Host.Playout;
using GenWave.Host.Tests.Support;
using GenWave.MediaLibrary.Options;

namespace GenWave.Host.Tests.Specs;

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real production composition root (Program.cs) with a fake, unreachable
/// <c>ConnectionStrings:Library</c> — the Story084/Story125 idiom (<c>StatusApiWebFactory</c>/
/// <c>LlmStatusWebFactory</c>): every option class Program.cs binds is genuinely wired and
/// boot-validated, but nothing on this file's own two ACs ever queries Postgres (GardenerOptions'
/// own <c>ValidateOnStart()</c> reads only IConfiguration), so a real ephemeral database would be
/// pure overhead here. <paramref name="gardenerOverrides"/> mirrors <c>Gardener__*</c> env vars as
/// colon-form <c>UseSetting</c> keys — a per-instance value with no shared process state (the
/// EnvVarMutatingWebFactoryCollection precedent: every spec that can reach Program.cs's
/// composition-time config reads this way does, reserving real env-var mutation for the one factory
/// that genuinely needs to prove ABSENCE).
/// </summary>
file sealed class GardenerKnobsWebFactory(params (string Key, string Value)[] gardenerOverrides)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story380-knobs";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development config provides Station:Id/Name/Voice/Scope/SafeScope and Tts:Endpoint so
        // ValidateOnStart() is satisfied without injecting them manually (the Story084/Story125
        // precedent this file's own header names).
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        foreach (var (key, value) in gardenerOverrides)
            builder.UseSetting($"Gardener:{key}", value);

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/DB-touching background loop during this test — mirrors every other
            // WebApplicationFactory-based spec in this suite (ScanService/EnrichmentService would
            // otherwise start against the fake connection string above).
            services.RemoveAll<IHostedService>();
        });
    }
}

// ---------------------------------------------------------------------
// HAPPY PATH — sane defaults, and the one Live knob flips without a restart
// ---------------------------------------------------------------------

public static class FeatureTheKnobsAndTheLiveSwitch
{
    public sealed class ScenarioDefaults
    {
        // Given no Gardener__* env, When the api boots.
        [Fact]
        public void TheBoundOptionsMatchTheDocumentedDefaults()
        {
            using var factory = new GardenerKnobsWebFactory();

            var options = factory.Services.GetRequiredService<IOptions<GardenerOptions>>().Value;

            Assert.Equal(0.5, options.NudgeGain);
            Assert.Equal(30, options.HalfLifeDays);
            Assert.Equal(5, options.Saturation);
            Assert.Equal(30, options.ThumbCooldownSeconds);
            Assert.Equal(60, options.ThumbDailyCap);
            Assert.Equal(90, options.ThumbRetentionDays);
            Assert.Equal(90, options.ShelfDustDays);
            Assert.Equal(2000, options.DuplicateToleranceMs);
            Assert.Equal(60, options.IntervalMinutes);
            Assert.Equal(500, options.BatchSize);
            Assert.False(options.FileActions.Enabled);
        }

        // LOW-5 (T357 review): FileActions.Enabled == false is indistinguishable, on its own, from
        // the nested Gardener:FileActions section never binding at all — this fact proves the nested
        // object genuinely binds a live config value rather than always reporting its C# default.
        [Fact]
        public void FileActionsEnabledGenuinelyBindsFromTheNestedSection()
        {
            using var factory = new GardenerKnobsWebFactory(("FileActions:Enabled", "true"));

            var options = factory.Services.GetRequiredService<IOptions<GardenerOptions>>().Value;

            Assert.True(options.FileActions.Enabled);
        }
    }

    [Collection(ThumbsLiveSwitchCollection.Name)]
    public sealed class ScenarioTheLiveSwitch(ThumbsLiveSwitchArc arc)
    {
        // Given Station:Thumbs:Enabled false, When PUT /api/settings sets it true.
        [Fact]
        public void TheNextThumbPostIsAcceptedWithNoRestart()
        {
            Assert.Equal(HttpStatusCode.NotFound, arc.StatusBeforeSwitch);
            Assert.Equal(HttpStatusCode.OK, arc.PutStatus);
            Assert.Equal(HttpStatusCode.Accepted, arc.StatusAfterSwitch);
        }
    }

    [Collection(ThumbsLiveSwitchCollection.Name)]
    public sealed class ScenarioDisclosure(ThumbsLiveSwitchArc arc)
    {
        // Given thumbs enabled, When the F67 disclosure suites run.
        [Fact]
        public void TheNowPlayingContractIsThePinnedSetPlusAiring()
        {
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                "title", "artist", "startedAt", "durationMs", "listeners", "dj", "djAvatarUrl",
                "show", "upNext", "artworkUrl", "airing", "state", "kind",
            };

            Assert.True(expected.SetEquals(arc.NowPlayingPropertyNames),
                $"expected exactly {{{string.Join(", ", expected)}}}, got {{{string.Join(", ", arc.NowPlayingPropertyNames)}}}");
        }

        [Fact]
        public void TheThumbsTwoOhTwoBodyIsThePinnedConstant() =>
            Assert.Equal(
                JsonSerializer.Serialize(new SpectatorThumbAccepted(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                arc.ThumbsAcceptedBodyText);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a bad knob never reaches a running station
    // ---------------------------------------------------------------------

    public sealed class ScenarioABadKnobFailsBoot
    {
        // Given Gardener__NudgeGain=7, When the api boots — the Announcements/RequestsOptions
        // ValidateOnStart idiom: a top-level [Range] violation fails the WHOLE host at startup,
        // never a running station with a silently-clamped or ignored knob.
        [Fact]
        public void ItExitsNamingTheOffendingKey()
        {
            using var factory = new GardenerKnobsWebFactory(("NudgeGain", "7"));

            var ex = Assert.Throws<OptionsValidationException>(() => factory.Services);

            Assert.Contains("NudgeGain", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ItExitsNamingTheAllowedRange()
        {
            using var factory = new GardenerKnobsWebFactory(("NudgeGain", "7"));

            var ex = Assert.Throws<OptionsValidationException>(() => factory.Services);

            // DataAnnotations' RangeAttribute own verbatim message text (net10 default format) —
            // the "allowed range" AC6 asks for, checked exactly rather than a loose "contains a
            // digit" proxy that would pass for any range at all.
            Assert.Contains("must be between 0 and 2", ex.Message, StringComparison.Ordinal);
        }

        // T357 review HIGH-1: RangeAttribute(int, int) on a double property (the original
        // [Range(0, 2)]) converts an out-of-range fractional config value via Convert.ToInt32
        // (MidpointRounding.ToEven, i.e. banker's rounding) BEFORE comparing against the bound —
        // 2.4 rounds to 2 (in range) while the real, un-rounded double property is left at 2.4,
        // silently exceeding the documented ceiling and over-weighting the rotation nudge.
        // RangeAttribute(double, double) (GardenerOptions.NudgeGain's fixed attribute) compares the
        // double directly, with no conversion step, so this value genuinely fails boot.
        [Fact]
        public void AFractionalValueJustAboveTheCeilingFailsBoot()
        {
            using var factory = new GardenerKnobsWebFactory(("NudgeGain", "2.4"));

            Assert.Throws<OptionsValidationException>(() => factory.Services);
        }

        // The floor's own mirror of the same HIGH-1 conversion trap: Convert.ToInt32(-0.5) rounds
        // to 0 (banker's rounding, midpoint-to-even) — in range under the buggy int-based
        // RangeAttribute, even though a negative NudgeGain would invert the thumbs signal (Score +=
        // Nudge × NudgeGain flips sign) rather than merely under-weighting it.
        [Fact]
        public void ANegativeFractionalValueBelowTheFloorFailsBoot()
        {
            using var factory = new GardenerKnobsWebFactory(("NudgeGain", "-0.5"));

            Assert.Throws<OptionsValidationException>(() => factory.Services);
        }
    }
}

// ── T366 write-path harness — AC2/AC5's own ephemeral station+library Postgres (the Story366/369
// EphemeralStationDatabase idiom); GardenerKnobsWebFactory above stays DB-free, unaffected. ────────

[CollectionDefinition(Name)]
public sealed class ThumbsLiveSwitchCollection : ICollectionFixture<ThumbsLiveSwitchArc>
{
    public const string Name = "Story380ThumbsLiveSwitch";
}

/// <summary>
/// AC2 + AC5: one logged-in session, one PUT /api/settings flip, one thumb POST, one now-playing
/// read — all against the SAME running host, proving the Live switch reaches the thumbs surface
/// with no api restart, and that turning it on changes nothing about either payload's own pinned
/// shape (SPEC F155.1's disjointness half). Every real POST in this arc runs exactly once, so the
/// per-IP Thumbs cooldown never trips between steps (unlike ThumbsWriteArc in Story369, which needs
/// several independent factories for exactly that reason).
/// </summary>
public sealed class ThumbsLiveSwitchArc : IAsyncLifetime
{
    public HttpStatusCode StatusBeforeSwitch { get; private set; }
    public HttpStatusCode PutStatus { get; private set; }
    public HttpStatusCode StatusAfterSwitch { get; private set; }
    public string ThumbsAcceptedBodyText { get; private set; } = "";
    public IReadOnlyList<string> NowPlayingPropertyNames { get; private set; } = [];

    public async Task InitializeAsync()
    {
        await using var db = await ThumbsLiveSwitchStationDatabase.StartAsync();
        var mediaId = await GardenerSeedFixtures.InsertMediaRowAsync(db.LibraryConnectionString, "/test/thumbs-live-switch.flac");

        await using var factory = new ThumbsLiveSwitchWebFactory(db);
        var client = factory.CreateClient();

        // Given Station:Thumbs:Enabled false (the deployment default, left unset below)...
        var beforeResponse = await client.PostAsJsonAsync(
            "/spectator/api/thumbs", new { airing = "AAAAAAAAAAAAAAAAAAAAAA", direction = "up" });
        StatusBeforeSwitch = beforeResponse.StatusCode;

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new { password = ThumbsLiveSwitchWebFactory.Password });
        if (login.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"login unexpectedly returned {login.StatusCode}");

        // When the REAL PUT /api/settings flips Station:Thumbs:Enabled — the genuine
        // SettingsController -> StationSettingsStore.WriteAsync path, real Postgres row + reload.
        var put = await client.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "Station:Thumbs:Enabled", value = "true" },
        });
        PutStatus = put.StatusCode;

        // Then the very NEXT thumb post is accepted — same running process, no restart.
        var sink = factory.Services.GetRequiredService<IStationEventSink>();
        var resolver = factory.Services.GetRequiredService<IAiringTokenResolver>();
        var startedAt = DateTimeOffset.Parse("2026-08-01T14:00:00Z");
        sink.Publish(new TrackAired(mediaId.ToString(), "Live Switch Song", "Live Switch Artist", 0.0, startedAt, 180_000));
        var token = resolver.Current ?? throw new InvalidOperationException("expected a minted token");

        var afterResponse = await client.PostAsJsonAsync(
            "/spectator/api/thumbs", new { airing = token, direction = "up" });
        StatusAfterSwitch = afterResponse.StatusCode;
        ThumbsAcceptedBodyText = await afterResponse.Content.ReadAsStringAsync();

        // AC5's now-playing half — the pinned property set is unaffected by thumbs being on. The
        // read needs NowPlayingService's own snapshot (the thumbs POST above never touches it).
        var store = factory.Services.GetRequiredService<NowPlayingService>();
        store.Update(SingleStation.IdString, new NowPlayingSnapshot(
            MediaId: mediaId.ToString(), Title: "Live Switch Song", Artist: "Live Switch Artist",
            GainDb: 0, StartedAt: startedAt, DurationMs: 180_000, IsDrain: false, Airing: token));

        var nowPlayingResponse = await client.GetAsync("/spectator/api/now-playing");
        var nowPlayingBody = JsonDocument.Parse(await nowPlayingResponse.Content.ReadAsStringAsync()).RootElement;
        NowPlayingPropertyNames = nowPlayingBody.EnumerateObject().Select(p => p.Name).ToList();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>This file's own <see cref="EphemeralStationDatabase"/> subclass — supplies only the
/// compose project-name prefix (the Story366/369 hoist precedent).</summary>
file sealed class ThumbsLiveSwitchStationDatabase : EphemeralStationDatabase
{
    ThumbsLiveSwitchStationDatabase(string project, string composeFile, string libraryConnectionString, string stationConnectionString)
        : base(project, composeFile, libraryConnectionString, stationConnectionString)
    {
    }

    public static async Task<ThumbsLiveSwitchStationDatabase> StartAsync()
    {
        var (project, composeFile, library, station) = Provision("genwave-thumbslive");
        var db = new ThumbsLiveSwitchStationDatabase(project, composeFile, library, station);
        await db.WaitForSchemaAsync();
        return db;
    }
}

/// <summary>
/// Boots the real production composition root against a real ephemeral Postgres, with
/// <c>Station:Thumbs:Enabled</c> deliberately UNSET (the deployment default, false) — AC2's own
/// "given the switch starts off" precondition; the arc's own PUT /api/settings is what flips it.
/// </summary>
file sealed class ThumbsLiveSwitchWebFactory(ThumbsLiveSwitchStationDatabase db) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story380-live-switch";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", db.LibraryConnectionString);
        builder.UseSetting("ConnectionStrings:Station", db.StationConnectionString);
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:SpectatorMode", "true");

        // The exact four Station:* keys compose.yaml itself overrides in production (Story366/369's
        // own precedent) — every other Station:* leaf rides appsettings.json's own shipped default.
        builder.UseSetting("Station:Id", "genwave-1");
        builder.UseSetting("Station:Name", "GWAV 108.8");
        builder.UseSetting("Station:Voice", "af_heart");
        builder.UseSetting("Station:Scope:LibraryIds:0", "1");
        // gh-#99: every media row this file seeds lands in the DEFAULT library (id 1,
        // db/01-library.sh's own `library_id ... default 1`) — Station:SafeScope:LibraryIds
        // defaults to [1] too (appsettings.json), which would silently exclude every one of
        // them from IThumbStore.RecordAsync as "safe scope" (ThumbWriteResult.Ignored). Point
        // the safe scope at a library id nothing here ever uses instead (the Story367
        // RotationNonMusicArc/Story355WebFactory own `safeLibraryId` precedent).
        builder.UseSetting("Station:SafeScope:LibraryIds:0", "999999");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}
