// STORY-359 — The house never leaks to a public stream (SPEC F145.1/.2 · PLAN T339 + T343)
//
// BDD specification — xUnit. ScenarioTheEndpointRefusesWhilePublic's two Facts are WIRED T339 — they
// drive the real production POST /api/announcements route through WebApplicationFactory<Program>
// with Station:SpectatorMode forced on, the same idiom Story357_AnnouncementEndpoint.cs uses.
// ScenarioGoingPublicDeclinesTheQueue stays Skip-tagged for PLAN T343 (the private→public transition
// guardian doesn't exist yet) — this file only fills in the endpoint's own half of F145.1/.2.

using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Announcements;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementPrivacy
{
    public sealed class ScenarioTheEndpointRefusesWhilePublic
    {
        [Fact]
        public async Task AValidPostUnderSpectatorModeIsAFourOhThreeWithAnHonestReason()
        {
            // Given a logged-in operator on a station with Station:SpectatorMode on
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementPrivacyWebFactory(store);
            var client = await AnnouncementPrivacyWebFactory.LoggedInClientAsync(factory);

            // When an otherwise-valid announcement posts
            var response = await client.PostAsJsonAsync("/api/announcements", new { message = "Dinner's ready" });

            // Then 403 with an honest reason naming the public-station cause
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(
                (Status: HttpStatusCode.Forbidden, NamesTheReason: true),
                (Status: response.StatusCode,
                 NamesTheReason: body.Contains("public", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public async Task NoRowIsCreatedByTheRefusedPost()
        {
            // Given a logged-in operator on a station with Station:SpectatorMode on
            var store = new FakeAnnouncementStore();
            await using var factory = new AnnouncementPrivacyWebFactory(store);
            var client = await AnnouncementPrivacyWebFactory.LoggedInClientAsync(factory);

            // When an otherwise-valid announcement posts
            await client.PostAsJsonAsync("/api/announcements", new { message = "Dinner's ready" });

            // Then the store's insert was never called — structurally impossible, not merely refused
            // after the fact (F145.1)
            Assert.Empty(store.InsertCalls);
        }
    }

    public sealed class ScenarioGoingPublicDeclinesTheQueue
    {
        [Fact]
        public async Task EveryPendingAnnouncementDeclinesAtThePrivateToPublicFlip()
        {
            // Given a pending announcement (id 101) live at the moment the station goes public...
            var (sink, drain, lifecycle, channel) = BuildFlipHarness();
            lifecycle.PendingIds.Add(101);

            // When the station writes Station:SpectatorMode=true — the SAME SettingChanged event
            // StationSettingsStore.WriteAsync publishes for every allowlisted write — and the drain
            // processes it...
            sink.Publish(new SettingChanged(AnnouncementPrivacyFlipEventSink.SpectatorModeKey));
            Assert.True(channel.Reader.TryRead(out var signal));
            await drain.ProcessAsync(signal!, CancellationToken.None);

            // Then the pending row declines — nothing is held waiting behind the toggle.
            Assert.Contains(101L, lifecycle.DeclinedIds);
        }

        [Fact]
        public async Task EveryClaimedAnnouncementDeclinesAtThePrivateToPublicFlip()
        {
            // Given a CLAIMED announcement (id 202) — already vended for delivery — live at the
            // moment the station goes public...
            var (sink, drain, lifecycle, channel) = BuildFlipHarness();
            lifecycle.ClaimedIds.Add(202);

            // When the station goes public...
            sink.Publish(new SettingChanged(AnnouncementPrivacyFlipEventSink.SpectatorModeKey));
            Assert.True(channel.Reader.TryRead(out var signal));
            await drain.ProcessAsync(signal!, CancellationToken.None);

            // Then the claimed row declines too — being already vended is no shelter from the flip.
            Assert.Contains(202L, lifecycle.DeclinedIds);
        }

        [Fact]
        public async Task TheDeclineReasonSaysTheStationWentPublic()
        {
            // Given the same flip...
            var (sink, drain, lifecycle, channel) = BuildFlipHarness();
            lifecycle.PendingIds.Add(101);

            sink.Publish(new SettingChanged(AnnouncementPrivacyFlipEventSink.SpectatorModeKey));
            Assert.True(channel.Reader.TryRead(out var signal));
            await drain.ProcessAsync(signal!, CancellationToken.None);

            // Then the stamped reason is exactly SPEC F145.2's own literal text.
            Assert.Equal("station went public", lifecycle.LastDeclineReason);
        }

        /// <summary>Builds the real sink/drain pair over a real bounded channel and a fresh
        /// <see cref="FakeAnnouncementLifecycle"/> — SpectatorMode starts (and stays, for this
        /// scenario's own purposes) ON, matching the moment <see cref="AnnouncementPrivacyFlipEventSink.Publish"/>
        /// itself reads live to decide whether a <c>SettingChanged</c> write is the private→public
        /// direction.</summary>
        static (AnnouncementPrivacyFlipEventSink Sink, AnnouncementPrivacyFlipDrainService Drain,
            FakeAnnouncementLifecycle Lifecycle, Channel<AnnouncementPrivacyFlipSignal> Channel) BuildFlipHarness()
        {
            var channel = Channel.CreateBounded<AnnouncementPrivacyFlipSignal>(4);
            var lifecycle = new FakeAnnouncementLifecycle();
            var options = new FakeOptionsMonitor<StationOptions>(new StationOptions { SpectatorMode = true });
            var sink = new AnnouncementPrivacyFlipEventSink(
                channel.Writer, options, NullLogger<AnnouncementPrivacyFlipEventSink>.Instance);
            var drain = new AnnouncementPrivacyFlipDrainService(
                channel.Reader, lifecycle, NullLogger<AnnouncementPrivacyFlipDrainService>.Instance);
            return (sink, drain, lifecycle, channel);
        }
    }

    // -------------------------------------------------------------------------
    // Scenario: the REAL SpectatorModeAnnouncementVendGuard (SPEC F145.2, PLAN T341 review finding
    // F5) — exercised directly against a mutable IOptionsMonitor<StationOptions>, never through the
    // WebApplicationFactory round trip above. The two facts above prove the DOOR (F145.1); these two
    // prove the vend-side refusal itself, live-read, unwrapped from any Host composition — the same
    // "boot-frozen snapshot would be a silent regression" risk this class's own remarks warn about.
    // -------------------------------------------------------------------------

    public sealed class ScenarioTheRealGuardReadsSpectatorModeLive
    {
        [Fact]
        public async Task WhileSpectatorModeIsOnTheClaimReadsEmptyAndTheInnerSourceIsNeverCalled()
        {
            // Given the real guard wrapping an inner source, with SpectatorMode on...
            var inner = new FakeInnerAnnouncementSource();
            var options = new FakeOptionsMonitor<StationOptions>(new StationOptions { SpectatorMode = true });
            var guard = new SpectatorModeAnnouncementVendGuard(inner, options);

            // When a claim is attempted...
            var claimed = await guard.ClaimDeliverableAsync(2, CancellationToken.None);

            // Then it reads back empty, AND the inner source was never reached — the refusal is
            // structural, not merely "the inner source happened to have nothing deliverable".
            Assert.Empty(claimed);
            Assert.Equal(0, inner.CallCount);
        }

        [Fact]
        public async Task FlippingSpectatorModeOffMidLifeStartsVendingOnTheVeryNextClaim()
        {
            // Given the real guard wrapping an inner source with one deliverable item queued, and
            // SpectatorMode initially on...
            var inner = new FakeInnerAnnouncementSource();
            inner.Items.Add(new AnnouncementItem(901, "Dinner's ready", Verbatim: true, RequestedVoice: null));
            var options = new FakeOptionsMonitor<StationOptions>(new StationOptions { SpectatorMode = true });
            var guard = new SpectatorModeAnnouncementVendGuard(inner, options);
            Assert.Empty(await guard.ClaimDeliverableAsync(2, CancellationToken.None));

            // When the station goes private mid-life — the SAME live-read seam a PUT /api/settings
            // write reaches in production, simulated here by mutating the monitor's own CurrentValue
            // in place, never by constructing a fresh guard...
            options.CurrentValue = new StationOptions { SpectatorMode = false };

            // Then the very next claim reaches the inner source and vends — no restart needed.
            var claimed = await guard.ClaimDeliverableAsync(2, CancellationToken.None);
            Assert.Single(claimed);
            Assert.Equal(1, inner.CallCount);
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own two T339-tagged Facts — mirrors
/// Story357_AnnouncementEndpoint.cs's own <c>AnnouncementsApiWebFactory</c> idiom exactly, plus forcing
/// <c>Station:SpectatorMode</c> on (SPEC F145.1's own live-read seam, <c>SurfaceGateMiddleware</c>'s
/// sibling check inside <c>AnnouncementsController</c> itself — see that class's own remarks).
/// </summary>
file sealed class AnnouncementPrivacyWebFactory(FakeAnnouncementStore store) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story359-privacy";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Station:SpectatorMode", "true");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IAnnouncementStore>();
            services.AddSingleton<IAnnouncementStore>(store);
        });
    }

    /// <summary>Logs in via the real POST /api/auth/login round trip. Mirrors Story357_AnnouncementEndpoint.cs's own helper.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }
}

/// <summary>
/// Minimal <see cref="IAnnouncementSource"/> double for <c>ScenarioTheRealGuardReadsSpectatorModeLive</c>
/// (T341 review finding F5) — records whether the REAL <see cref="SpectatorModeAnnouncementVendGuard"/>
/// ever reached through to it, which the WebApplicationFactory-level facts above cannot observe
/// (<see cref="FakeAnnouncementStore"/> stands in one seam over, <see cref="IAnnouncementStore"/>, never
/// this narrower vend-only seam the guard itself wraps).
/// </summary>
file sealed class FakeInnerAnnouncementSource : IAnnouncementSource
{
    public List<AnnouncementItem> Items { get; } = [];
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<AnnouncementItem>> ClaimDeliverableAsync(int max, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult<IReadOnlyList<AnnouncementItem>>(Items.Take(max).ToList());
    }
}
