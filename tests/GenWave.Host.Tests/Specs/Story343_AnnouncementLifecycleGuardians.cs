// STORY-358/359 — Lifecycle guardians (SPEC F143.2/.3, F144.5/.6, F145.2 · PLAN T343)
//
// Facts not already owned by Story358_AnnouncementAirConfirmation.cs / Story359_AnnouncementPrivacy.cs:
// the sweep's own ordering guarantee, and the "a sink must never throw" contract every
// IStationEventSink implementation promises (IStationEventSink's own remarks).
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Announcements;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementLifecycleGuardians
{
    public sealed class ScenarioTheSweepOrdersExpiryBeforeReArm
    {
        [Fact]
        public async Task ExpireStaleAsyncRunsBeforeFindClaimedPastGraceAsync()
        {
            // Given a sweep with something to do on both sides...
            var lifecycle = new FakeAnnouncementLifecycle { ExpireStaleResult = 1, ClaimedPastGraceResult = [42] };
            lifecycle.ReArmSucceedsFor.Add(42);
            var guardian = new AnnouncementLifecycleGuardianService(
                lifecycle, TimeProvider.System, NullLogger<AnnouncementLifecycleGuardianService>.Instance);

            // When one sweep runs...
            await guardian.SweepOnceAsync(CancellationToken.None);

            // Then ExpireStaleAsync's call landed strictly before FindClaimedPastGraceAsync's — the
            // load-bearing order this class's own remarks name (F144.5's "TTL permitting" clause: a
            // TTL-passed claimed row must expire, never re-arm, which only holds if expiry ran first).
            var expireIndex = lifecycle.CallOrder.IndexOf(nameof(FakeAnnouncementLifecycle.ExpireStaleAsync));
            var findIndex = lifecycle.CallOrder.IndexOf(nameof(FakeAnnouncementLifecycle.FindClaimedPastGraceAsync));
            Assert.True(expireIndex >= 0 && findIndex >= 0 && expireIndex < findIndex);
        }

        [Fact]
        public async Task TheSameNowInstantIsUsedForBothCallsOnOneSweep()
        {
            // Given a sweep driven by a fixed clock...
            var lifecycle = new FakeAnnouncementLifecycle();
            var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-22T12:00:00Z"));
            var guardian = new AnnouncementLifecycleGuardianService(
                lifecycle, time, NullLogger<AnnouncementLifecycleGuardianService>.Instance);

            // When one sweep runs...
            await guardian.SweepOnceAsync(CancellationToken.None);

            // Then both calls read the exact same instant — no re-reading the clock mid-sweep, which
            // could otherwise let a row's TTL pass BETWEEN the expiry check and the re-arm candidate
            // read within the very same tick.
            Assert.Equal(time.GetUtcNow(), Assert.Single(lifecycle.ExpireStaleCalls));
            Assert.Equal(time.GetUtcNow(), Assert.Single(lifecycle.FindClaimedPastGraceCalls).Now);
        }
    }

    public sealed class ScenarioSinksNeverThrow
    {
        [Fact]
        public void TheAiredSinkNeverThrowsForAnUnrelatedEvent()
        {
            var channel = Channel.CreateBounded<AnnouncementAiredSignal>(1);
            var sink = new AnnouncementAiredEventSink(channel.Writer, NullLogger<AnnouncementAiredEventSink>.Instance);

            var ex = Record.Exception(() => sink.Publish(new SettingChanged("Some:Unrelated:Key")));

            Assert.Null(ex);
        }

        [Fact]
        public void TheAiredSinkNeverThrowsWhenItsQueueIsFull()
        {
            var channel = Channel.CreateBounded<AnnouncementAiredSignal>(1);
            Assert.True(channel.Writer.TryWrite(new AnnouncementAiredSignal(1))); // fill the one slot
            var sink = new AnnouncementAiredEventSink(channel.Writer, NullLogger<AnnouncementAiredEventSink>.Instance);
            var mediaId = AnnouncementMediaId.Wrap(2, "tts:xyz");

            var ex = Record.Exception(() => sink.Publish(new TrackAired(
                mediaId, null, null, 0, DateTimeOffset.UtcNow, null, SegmentKind: SegmentKind.Announcement)));

            Assert.Null(ex);
        }

        [Fact]
        public void TheAiredSinkNeverThrowsForAMalformedMediaId()
        {
            // A SegmentKind.Announcement TrackAired whose MediaId was never AnnouncementMediaId.Wrap'd
            // — TryUnwrap answers false, and Publish must simply do nothing, never throw.
            var channel = Channel.CreateBounded<AnnouncementAiredSignal>(1);
            var sink = new AnnouncementAiredEventSink(channel.Writer, NullLogger<AnnouncementAiredEventSink>.Instance);

            var ex = Record.Exception(() => sink.Publish(new TrackAired(
                "tts:not-wrapped", null, null, 0, DateTimeOffset.UtcNow, null, SegmentKind: SegmentKind.Announcement)));

            Assert.Null(ex);
            Assert.False(channel.Reader.TryRead(out _));
        }

        [Fact]
        public void ThePrivacyFlipSinkNeverThrowsForAnUnrelatedEvent()
        {
            var channel = Channel.CreateBounded<AnnouncementPrivacyFlipSignal>(1);
            var options = new FakeOptionsMonitor<StationOptions>(new StationOptions { SpectatorMode = true });
            var sink = new AnnouncementPrivacyFlipEventSink(
                channel.Writer, options, NullLogger<AnnouncementPrivacyFlipEventSink>.Instance);

            var ex = Record.Exception(() => sink.Publish(new SettingChanged("Station:Theme")));

            Assert.Null(ex);
            Assert.False(channel.Reader.TryRead(out _));
        }

        [Fact]
        public void ThePrivacyFlipSinkNeverThrowsWhenItsQueueIsFull()
        {
            var channel = Channel.CreateBounded<AnnouncementPrivacyFlipSignal>(1);
            Assert.True(channel.Writer.TryWrite(new AnnouncementPrivacyFlipSignal())); // fill the one slot
            var options = new FakeOptionsMonitor<StationOptions>(new StationOptions { SpectatorMode = true });
            var sink = new AnnouncementPrivacyFlipEventSink(
                channel.Writer, options, NullLogger<AnnouncementPrivacyFlipEventSink>.Instance);

            var ex = Record.Exception(
                () => sink.Publish(new SettingChanged(AnnouncementPrivacyFlipEventSink.SpectatorModeKey)));

            Assert.Null(ex);
        }
    }
}
