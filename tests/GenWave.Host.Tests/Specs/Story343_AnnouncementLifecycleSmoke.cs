// STORY-358 — Smoke: the production DI graph really wires the guardians together (PLAN T343)
//
// Unlike Story358_AnnouncementAirConfirmation.cs / Story343_AnnouncementLifecycleGuardians.cs (which
// hand-construct AnnouncementAiredEventSink/AnnouncementAiredDrainService directly), this file resolves
// the REAL IStationEventSink Program.cs's own composition root produces — proving
// AnnouncementLifecycleHostServiceCollectionExtensions and PlayoutServiceCollectionExtensions actually
// compose these types together, not merely that each type works in isolation when a test wires it by
// hand. Mirrors Story185_CorrectionsLiveWiring.cs's own "fake only the DB/network edges this suite
// cannot reach" convention: only IAnnouncementLifecycle and IBoothLogAppender (the two Postgres-backed
// leaves) are swapped; every other registration — AddGenWaveAnnouncementLifecycle, AddGenWavePlayout's
// CompositeStationEventSink — is the genuine production wiring.
//
// Hosted services are removed (no Liquidsoap/Kokoro/Postgres reach during this test — the same
// "RemoveAll<IHostedService>()" every WebApplicationFactory-based spec in this suite applies), so the
// drain service is re-registered as itself (in ADDITION to its IHostedService wrapping, which
// AddHostedService<T> never exposes for direct resolution) purely for this test's own ability to call
// ProcessAsync directly — the same directly-testable-without-the-real-loop seam
// BoothLogDrainService.ProcessAsync/CrosstalkStockWorker.TickOnceAsync already establish.

using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

            // The three Postgres-backed edges this suite cannot reach.
            services.RemoveAll<IAnnouncementLifecycle>();
            services.AddSingleton<IAnnouncementLifecycle>(lifecycle);

            services.RemoveAll<IBoothLogAppender>();
            services.AddSingleton(boothLog);
        });
    }
}
