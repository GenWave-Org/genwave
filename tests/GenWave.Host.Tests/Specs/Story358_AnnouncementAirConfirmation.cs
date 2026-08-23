// STORY-358 — The DJ says it: two fidelities, one fallback (SPEC F143.3, F144.5/.6 · PLAN T343)
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Announcements;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAnnouncementAirConfirmation
{
    public sealed class ScenarioAiredMeansObservedOnAir
    {
        [Fact]
        public async Task ATrackAiredObservationOfTheSegmentStampsAired()
        {
            // Given a claimed announcement's own rendered segment about to reach air, wrapped the way
            // Orchestrator.EnqueuePatterAsync stamps it (SPEC F144.1)...
            var channel = Channel.CreateBounded<AnnouncementAiredSignal>(8);
            var lifecycle = new FakeAnnouncementLifecycle();
            var boothLog = new FakeBoothLogAppender();
            var sink = new AnnouncementAiredEventSink(channel.Writer, NullLogger<AnnouncementAiredEventSink>.Instance);
            var drain = new AnnouncementAiredDrainService(
                channel.Reader, lifecycle, boothLog, NullLogger<AnnouncementAiredDrainService>.Instance);
            var mediaId = AnnouncementMediaId.Wrap(555, "tts:abc");

            // When the REAL production event — a genuine TrackAired for that exact segment — publishes...
            sink.Publish(new TrackAired(
                mediaId, "Dinner's ready", null, 0.0, DateTimeOffset.UtcNow, 4200, SegmentKind: SegmentKind.Announcement));
            Assert.True(channel.Reader.TryRead(out var signal));
            await drain.ProcessAsync(signal!, CancellationToken.None);

            // Then the store's own aired transition was reached for exactly that announcement id.
            Assert.Contains(555L, lifecycle.MarkAiredCalls);
        }

        [Fact]
        public async Task OneBoothLogEntryCarriesTheCollapseCount()
        {
            // Given an announcement that collapsed three submissions into one row before it claimed...
            var channel = Channel.CreateBounded<AnnouncementAiredSignal>(8);
            var lifecycle = new FakeAnnouncementLifecycle();
            lifecycle.CollapseCountByAnnouncementId[555] = 3;
            var boothLog = new FakeBoothLogAppender();
            var sink = new AnnouncementAiredEventSink(channel.Writer, NullLogger<AnnouncementAiredEventSink>.Instance);
            var drain = new AnnouncementAiredDrainService(
                channel.Reader, lifecycle, boothLog, NullLogger<AnnouncementAiredDrainService>.Instance);
            var mediaId = AnnouncementMediaId.Wrap(555, "tts:abc");

            // When it airs...
            sink.Publish(new TrackAired(
                mediaId, "Dinner's ready", null, 0.0, DateTimeOffset.UtcNow, 4200, SegmentKind: SegmentKind.Announcement));
            Assert.True(channel.Reader.TryRead(out var signal));
            await drain.ProcessAsync(signal!, CancellationToken.None);

            // Then exactly one booth_log 'announcement-aired' entry exists, and it carries the collapse count.
            var entry = Assert.Single(boothLog.Calls);
            Assert.Equal("announcement-aired", entry.Kind);
            Assert.Contains("3", entry.Summary, StringComparison.Ordinal);
        }

        [Fact]
        public async Task APushAloneNeverStampsAired()
        {
            // Given the SAME sink/drain pair, with an announcement genuinely claimed (id 555)...
            var channel = Channel.CreateBounded<AnnouncementAiredSignal>(8);
            var lifecycle = new FakeAnnouncementLifecycle();
            var sink = new AnnouncementAiredEventSink(channel.Writer, NullLogger<AnnouncementAiredEventSink>.Instance);

            // When every OTHER event this station's playout ever publishes fires — a music track
            // starting, and a DIFFERENT segment kind entirely — none of which is a genuine TrackAired
            // observation of THIS announcement's own segment (there is no "pushed" event at all in
            // this codebase; the engine push itself never reaches IStationEventSink — only a later,
            // genuine advance confirmation does)...
            sink.Publish(new TrackAired("42", "Some Song", "Some Artist", 0.0, DateTimeOffset.UtcNow, 180_000));
            sink.Publish(new TrackAired(
                "tts:hash-only", null, null, 0.0, DateTimeOffset.UtcNow, 1200, SegmentKind: SegmentKind.BackAnnounce));

            // Then nothing was ever enqueued for confirmation, and the store's aired transition was
            // never reached — a push (or any non-matching event) alone can never stamp aired.
            Assert.False(channel.Reader.TryRead(out _));
            Assert.Empty(lifecycle.MarkAiredCalls);
        }
    }

    public sealed class ScenarioPushLossReArms
    {
        [Fact]
        public async Task AClaimedAnnouncementUnairedPastTheGraceReturnsToPending()
        {
            // Given a claimed announcement (id 777) whose own claim grace has passed, with TTL still
            // remaining — the sweep's expiry pass found nothing to expire this tick...
            var lifecycle = new FakeAnnouncementLifecycle
            {
                ClaimedPastGraceResult = [777],
            };
            lifecycle.ReArmSucceedsFor.Add(777);
            var guardian = new AnnouncementLifecycleGuardianService(
                lifecycle, TimeProvider.System, NullLogger<AnnouncementLifecycleGuardianService>.Instance);

            // When the lifecycle guardian sweeps...
            await guardian.SweepOnceAsync(CancellationToken.None);

            // Then it returns to pending — ReArmAsync was reached for exactly that id.
            Assert.Contains(777L, lifecycle.ReArmCalls);
        }

        [Fact]
        public async Task AReArmWithNoTtlRemainingExpiresVisiblyInstead()
        {
            // Given a claimed announcement whose TTL has ALREADY passed — ExpireStaleAsync (run FIRST
            // by the guardian's own ordering) would already have expired it, so it is never a member
            // of FindClaimedPastGraceAsync's own result set...
            var lifecycle = new FakeAnnouncementLifecycle
            {
                ExpireStaleResult = 1,
                ClaimedPastGraceResult = [],
            };
            var guardian = new AnnouncementLifecycleGuardianService(
                lifecycle, TimeProvider.System, NullLogger<AnnouncementLifecycleGuardianService>.Instance);

            // When the lifecycle guardian sweeps...
            await guardian.SweepOnceAsync(CancellationToken.None);

            // Then nothing re-arms — the row expired visibly instead, never silently vanished.
            Assert.Empty(lifecycle.ReArmCalls);
            Assert.Single(lifecycle.ExpireStaleCalls);
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// In-memory <see cref="GenWave.Core.Abstractions.IBoothLogAppender"/> double — records every
/// <see cref="AppendAsync"/> call's <see cref="GenWave.Core.Domain.BoothLogAppendRequest"/> instead of
/// touching Postgres. Mirrors Story217_BoothLogPickStamp.cs's own <c>FakeBoothLogAppender</c> idiom,
/// deliberately re-declared here (rather than shared) since that one is <see langword="file"/>-scoped
/// to its own file.
/// </summary>
file sealed class FakeBoothLogAppender : GenWave.Core.Abstractions.IBoothLogAppender
{
    public List<GenWave.Core.Domain.BoothLogAppendRequest> Calls { get; } = [];

    public Task AppendAsync(GenWave.Core.Domain.BoothLogAppendRequest request, CancellationToken ct)
    {
        Calls.Add(request);
        return Task.CompletedTask;
    }
}
