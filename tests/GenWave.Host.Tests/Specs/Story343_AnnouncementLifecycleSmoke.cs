// STORY-358/359 — Smoke: the production DI graph really wires the guardians together (PLAN T343)
//
// Unlike Story358_AnnouncementAirConfirmation.cs / Story359_AnnouncementPrivacy.cs / Story343_
// AnnouncementLifecycleGuardians.cs (which hand-construct AnnouncementAiredEventSink/
// AnnouncementAiredDrainService/AnnouncementPrivacyFlipEventSink/AnnouncementPrivacyFlipDrainService
// directly), this file resolves the REAL IStationEventSink Program.cs's own composition root
// produces — proving AnnouncementLifecycleHostServiceCollectionExtensions and
// PlayoutServiceCollectionExtensions actually compose these types together, not merely that each
// type works in isolation when a test wires it by hand. Mirrors Story185_CorrectionsLiveWiring.cs's
// own "fake only the DB/network edges this suite cannot reach" convention: only IAnnouncementLifecycle
// and IBoothLogAppender (the two Postgres-backed leaves) and IStationSettingsStore (the settings
// overlay's own Postgres leaf) are swapped; every other registration — AddGenWaveAnnouncementLifecycle,
// AddGenWavePlayout's CompositeStationEventSink, SettingsController, the real allowlist/validator — is
// the genuine production wiring.
//
// Hosted services are removed (no Liquidsoap/Kokoro/Postgres reach during this test — the same
// "RemoveAll<IHostedService>()" every WebApplicationFactory-based spec in this suite applies), so the
// drain services are re-registered as themselves (in ADDITION to their IHostedService wrapping, which
// AddHostedService<T> never exposes for direct resolution) purely for this test's own ability to call
// ProcessAsync directly — the same directly-testable-without-the-real-loop seam
// BoothLogDrainService.ProcessAsync/CrosstalkStockWorker.TickOnceAsync already establish.

using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Announcements;
using GenWave.Host.Configuration;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementLifecycleSmoke
{
    public sealed class ScenarioAiredConfirmationThroughTheRealSinkComposition
    {
        [Fact]
        public async Task AWrappedMediaIdPublishedAsTrackAiredStampsAiredAndWritesOneBoothRow()
        {
            // Given the real host, with only the two Postgres-backed leaves faked...
            var lifecycle = new FakeAnnouncementLifecycle();
            lifecycle.CollapseCountByAnnouncementId[555] = 1;
            var boothLog = new FakeBoothLogAppender();
            await using var factory = new AnnouncementLifecycleSmokeWebFactory(lifecycle, boothLog);
            var services = factory.Services; // builds the host without starting Kestrel

            // When a claimed announcement's rendered segment reaches air — the genuine production
            // signal, published through the REAL, fully-composed IStationEventSink (never a
            // hand-built AnnouncementAiredEventSink)...
            var sink = services.GetRequiredService<IStationEventSink>();
            var mediaId = AnnouncementMediaId.Wrap(555, "tts:abc");
            sink.Publish(new TrackAired(
                mediaId, "Dinner's ready", null, 0.0, DateTimeOffset.UtcNow, 4200, SegmentKind: SegmentKind.Announcement));

            // Then the REAL queue (also container-resolved) carries the confirmation, and draining it
            // through the REAL drain service reaches the aired transition and writes one booth row.
            var reader = services.GetRequiredService<ChannelReader<AnnouncementAiredSignal>>();
            Assert.True(reader.TryRead(out var signal));
            var drain = services.GetRequiredService<AnnouncementAiredDrainService>();
            await drain.ProcessAsync(signal!, CancellationToken.None);

            Assert.Contains(555L, lifecycle.MarkAiredCalls);
            var entry = Assert.Single(boothLog.Calls);
            Assert.Equal("announcement-aired", entry.Kind);
        }
    }

    public sealed class ScenarioSpectatorModeFlipThroughTheRealSettingsPut
    {
        [Fact]
        public async Task FlippingSpectatorModeViaTheRealSettingsPutDeclinesPendingAnnouncements()
        {
            // Given the real host, with a pending announcement (id 101) live, and a logged-in
            // operator...
            var lifecycle = new FakeAnnouncementLifecycle();
            lifecycle.PendingIds.Add(101);
            var boothLog = new FakeBoothLogAppender();
            await using var factory = new AnnouncementLifecycleSmokeWebFactory(lifecycle, boothLog);
            var client = factory.CreateClient();

            var login = await client.PostAsJsonAsync(
                "/api/auth/login", new { password = AnnouncementLifecycleSmokeWebFactory.Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            // When the REAL PUT /api/settings flips Station:SpectatorMode — the exact wire T345
            // exercises live — through the genuine SettingsController -> StationSettingsStore.WriteAsync
            // path (LiveTestSettingsStore below mirrors that write's own two real side effects: the
            // IConfiguration reload AND the SettingChanged publish through the real IStationEventSink)...
            var put = await client.PutAsJsonAsync("/api/settings", new[]
            {
                new { key = "Station:SpectatorMode", value = "true" },
            });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            // Then the REAL flip queue carries the signal, and draining it through the REAL drain
            // service declines the pending row.
            var reader = factory.Services.GetRequiredService<ChannelReader<AnnouncementPrivacyFlipSignal>>();
            Assert.True(reader.TryRead(out var signal));
            var drain = factory.Services.GetRequiredService<AnnouncementPrivacyFlipDrainService>();
            await drain.ProcessAsync(signal!, CancellationToken.None);

            Assert.Contains(101L, lifecycle.DeclinedIds);
            Assert.Equal("station went public", lifecycle.LastDeclineReason);
        }
    }
}

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="IStationSettingsStore"/> test double standing in for the one thing this non-Integration
/// suite cannot reach — a live Postgres <c>station.settings</c> table. Mirrors
/// Story185_CorrectionsLiveWiring.cs's own <c>LiveTestSettingsStore</c> shape (a live
/// <see cref="IConfiguration"/> reload, the same change-token signal <c>StationSettingsConfigurationProvider.Reload</c>
/// raises in production), EXTENDED here with the SECOND real side effect that file's own fake never
/// needed: <see cref="WriteAsync"/> ALSO publishes <see cref="SettingChanged"/> through the real
/// <see cref="IStationEventSink"/> — the exact signal <c>AnnouncementPrivacyFlipEventSink</c> listens
/// for, and the one <c>StationSettingsStore.WriteAsync</c> itself fires in production (gitea-#246).
/// </summary>
file sealed class LiveTestSettingsStore : IStationSettingsStore
{
    readonly LiveTestConfigurationProvider provider = new();
    readonly IStationEventSink events;

    public LiveTestSettingsStore(IConfiguration configuration, IStationEventSink events)
    {
        ((IConfigurationBuilder)configuration).Add(new LiveTestConfigurationSource(provider));
        this.events = events;
    }

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        if (!StationSettingsAllowlist.ByKey.ContainsKey(key))
            throw new ArgumentException($"Key '{key}' is not on the station settings allowlist.", nameof(key));

        provider.SetAndReload(key, value?.ToString() ?? string.Empty);
        events.Publish(new SettingChanged(key));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>See <see cref="LiveTestSettingsStore"/>'s own remarks — mirrors Story185's identical
/// helper pair one-for-one.</summary>
file sealed class LiveTestConfigurationProvider : ConfigurationProvider
{
    public void SetAndReload(string key, string value)
    {
        Set(key, value);
        OnReload();
    }
}

file sealed class LiveTestConfigurationSource(LiveTestConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Boots the real host with a valid admin password (cookie auth) and the three fakes above swapped in
/// — everything else (routing, auth, <c>SettingsController</c>, <c>AnnouncementLifecycleHostServiceCollectionExtensions</c>,
/// <c>PlayoutServiceCollectionExtensions</c>'s <c>CompositeStationEventSink</c>) is the genuine
/// production wiring. Mirrors Story185's <c>CorrectionsLiveWiringWebFactory</c>/Story359's own
/// <c>AnnouncementPrivacyWebFactory</c> shape.
/// </summary>
file sealed class AnnouncementLifecycleSmokeWebFactory(
    FakeAnnouncementLifecycle lifecycle, IBoothLogAppender boothLog) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story343-smoke";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting(StationSettingsHostingExtensions.ExpectNoStoreKey, "true");

        builder.ConfigureTestServices(services =>
        {
            // No Liquidsoap/Kokoro/Postgres reach during this test.
            services.RemoveAll<IHostedService>();

            // Re-registered as themselves (AddHostedService<T> never exposes T for direct
            // resolution) — see this file's own header remarks.
            services.AddSingleton<AnnouncementAiredDrainService>();
            services.AddSingleton<AnnouncementPrivacyFlipDrainService>();

            // The three Postgres-backed edges this suite cannot reach.
            services.RemoveAll<IAnnouncementLifecycle>();
            services.AddSingleton<IAnnouncementLifecycle>(lifecycle);

            services.RemoveAll<IBoothLogAppender>();
            services.AddSingleton(boothLog);

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(sp =>
                new LiveTestSettingsStore(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IStationEventSink>()));
        });
    }
}
