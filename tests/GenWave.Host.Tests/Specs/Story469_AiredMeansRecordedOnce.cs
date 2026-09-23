// STORY-469 — Aired means recorded once (gh-#773 · SPEC F202 · PLAN T554)
//
// BDD specification — xUnit. Retry math throughout rides Microsoft.Extensions.TimeProvider.Testing's
// FakeTimeProvider (it DOES implement CreateTimer, so AnnouncementAiredDrainService's own
// Task.Delay(delay, timeProvider, ct) rides Advance() rather than a real wall-clock wait — same idiom
// as Story343_AnnouncementLifecycleGuardians.cs and GenWave.Ads' Story442).

using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Announcements;
using GenWave.Host.Tests.Fakes;
using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAiredmeansrecordedonce
{
    /// <summary>A <see cref="FakeTimeProvider"/> that also counts every timer
    /// <see cref="CreateTimer"/> has handed out, and records the <c>dueTime</c> each one was created
    /// with — the fake clock's own <see cref="FakeTimeProvider.Advance"/> only ever fires a timer
    /// that ALREADY EXISTS, but the drain's own <c>Task.Delay(delay, timeProvider, ct)</c> creates
    /// that timer on a different async continuation (after the scripted throw, the retry catch
    /// filter, and a <c>LogDebug</c> call all run) than the scenario's own thread. Advancing before
    /// that timer is created leaves it due entirely in the past — it never fires, and <c>await
    /// run</c> hangs. <see cref="WaitForTimerAsync"/> lets a scenario block (via <see
    /// cref="Task.Yield"/>, never a real sleep) until the expected timer genuinely exists before
    /// calling <see cref="FakeTimeProvider.Advance"/>, with a real-time deadline as a safety net
    /// against a genuine regression hanging the fact rather than a real wait in the happy path. The
    /// count (and <see cref="DueTimes"/>) only ever reaches <c>N</c> once the <c>N</c>th timer is
    /// registered with the fake clock — <c>base.CreateTimer</c> MUST run before either is updated, or
    /// <see cref="WaitForTimerAsync"/> can return and a caller's <see cref="FakeTimeProvider.Advance"/>
    /// can run in the gap before the timer is actually registered, so it fires against a clock that
    /// has already moved past its due time and never elapses.</summary>
    sealed class CountingFakeTimeProvider(DateTimeOffset startTime) : FakeTimeProvider(startTime)
    {
        readonly List<TimeSpan> dueTimes = [];
        int timersCreated;

        public IReadOnlyList<TimeSpan> DueTimes { get { lock (dueTimes) return dueTimes.ToList(); } }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);   // registered with the fake clock FIRST
            lock (dueTimes) dueTimes.Add(dueTime);
            Interlocked.Increment(ref timersCreated);                        // only then visible to WaitForTimerAsync
            return timer;
        }

        public async Task WaitForTimerAsync(int expectedCount, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (Volatile.Read(ref timersCreated) < expectedCount)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException(
                        $"expected {expectedCount} timer(s) to be created, saw {Volatile.Read(ref timersCreated)}");
                await Task.Yield();
            }
        }
    }

    public sealed class ScenarioAFullQueue
    {
        /// <summary>AC1 — TryWrite returns true</summary>
        [Fact]
        public void CannotRefuseASignal()
        {
            // Given the real sink over production's own unbounded channel shape (SPEC F202.1,
            // SingleReader=true — matches AnnouncementLifecycleHostServiceCollectionExtensions'
            // own registration exactly) already holding 10,000 queued signals. A SingleReader
            // channel's Reader.Count throws NotSupportedException (no O(1) count is kept when only
            // one consumer will ever drain it), so this fact counts by draining with TryRead rather
            // than reading Count...
            var channel = Channel.CreateUnbounded<AnnouncementAiredSignal>(
                new UnboundedChannelOptions { SingleReader = true });
            for (var i = 0; i < 10_000; i++)
                Assert.True(channel.Writer.TryWrite(new AnnouncementAiredSignal(i)));
            var sink = new AnnouncementAiredEventSink(channel.Writer, NullLogger<AnnouncementAiredEventSink>.Instance);
            var mediaId = AnnouncementMediaId.Wrap(10_001, "tts:xyz");

            // When one more signal arrives...
            sink.Publish(new TrackAired(
                mediaId, "Dinner's ready", null, 0.0, DateTimeOffset.UtcNow, 4200, SegmentKind: SegmentKind.Announcement));

            // Then it was accepted, never refused — Publish is void (TryWrite's own bool return isn't
            // observable through it), so draining the queue and counting what comes out IS the
            // "TryWrite returned true" fact made visible: an unbounded channel has no capacity to
            // refuse against.
            var count = 0;
            while (channel.Reader.TryRead(out _)) count++;
            Assert.Equal(10_001, count);
        }
    }

    // AC2 (aired_at unchanged across a duplicate MarkAiredAsync) is a real-Postgres fact on the
    // repository, not the Host drain — moved to
    // tests/GenWave.MediaLibrary.Tests/Specs/Story469_MarkAiredIsIdempotent.cs (Host.Tests has no
    // DatabaseFixture; that suite already owns AnnouncementRepository's own facts, per Story357).

    public sealed class ScenarioTwoSignalsForOneId
    {
        /// <summary>AC3 — no WARN</summary>
        [Fact]
        public async Task StaysSilentOnADuplicate()
        {
            // Given the drain with a recording logger, and an announcement about to air twice — the
            // second MarkAiredAsync call answers null (SPEC F202.3's idempotent WHERE, standing in
            // here via AiredOutcomeIsNull, mirrors Story358's own replay scenario)...
            var lifecycle = new FakeAnnouncementLifecycle();
            var boothLog = new FakeBoothLogAppender();
            var provider = new CapturingLoggerProvider();
            using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider));
            var drain = new AnnouncementAiredDrainService(
                Channel.CreateUnbounded<AnnouncementAiredSignal>().Reader, lifecycle, boothLog, TimeProvider.System,
                loggerFactory.CreateLogger<AnnouncementAiredDrainService>());
            var signal = new AnnouncementAiredSignal(555);

            // When the drain processes the same signal twice...
            await drain.ProcessAsync(signal, CancellationToken.None);
            lifecycle.AiredOutcomeIsNull.Add(555);
            await drain.ProcessAsync(signal, CancellationToken.None);

            // Then nothing was ever logged at Warning or above — a duplicate is a normal, silent
            // outcome (SPEC F202.3), never a fault.
            Assert.Empty(provider.Messages);
        }
    }

    public sealed class ScenarioAStoreThatBlinksOnce
    {
        /// <summary>AC4 — marked by t = 1 s</summary>
        [Fact]
        public async Task RecoversOnTheFirstRetry()
        {
            // Given a store that throws on the first write and succeeds on the second, and a fake
            // clock standing in for wall-clock time...
            var lifecycle = new FakeAnnouncementLifecycle { MarkAiredFailuresRemaining = 1 };
            var boothLog = new FakeBoothLogAppender();
            var time = new CountingFakeTimeProvider(DateTimeOffset.Parse("2026-09-23T12:00:00Z"));
            var drain = new AnnouncementAiredDrainService(
                Channel.CreateUnbounded<AnnouncementAiredSignal>().Reader, lifecycle, boothLog, time,
                NullLogger<AnnouncementAiredDrainService>.Instance);
            var signal = new AnnouncementAiredSignal(555);

            // When the drain runs — the first attempt fails, the retry delay's own timer is let
            // register, then fake time is advanced past it...
            var run = drain.ProcessAsync(signal, CancellationToken.None);
            await time.WaitForTimerAsync(1, TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(1));
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            // Then the row was marked aired on the retry — a second attempt was reached, at t = 1 s.
            Assert.Equal(2, lifecycle.MarkAiredCalls.Count);
        }
    }

    public sealed class ScenarioTheGuardianGrace
    {
        /// <summary>AC5 — 6 minutes</summary>
        [Fact]
        public void LeavesTheGraceAlone() =>
            Assert.Equal(TimeSpan.FromMinutes(6), AnnouncementLifecycleGuardianService.ReArmGrace);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAStoreThatKeepsFailing : IAsyncLifetime
    {
        readonly FakeAnnouncementLifecycle lifecycle = new() { MarkAiredFailuresRemaining = 4 };
        readonly CapturingLoggerProvider provider = new();
        readonly CountingFakeTimeProvider time = new(DateTimeOffset.Parse("2026-09-23T12:00:00Z"));
        const long AnnouncementId = 555;

        public async Task InitializeAsync()
        {
            // Given a store that throws four times in a row — one more than the drain's own three
            // retries can absorb (SPEC F202.2)...
            var boothLog = new FakeBoothLogAppender();
            using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider));
            var drain = new AnnouncementAiredDrainService(
                Channel.CreateUnbounded<AnnouncementAiredSignal>().Reader, lifecycle, boothLog, time,
                loggerFactory.CreateLogger<AnnouncementAiredDrainService>());
            var signal = new AnnouncementAiredSignal(AnnouncementId);

            // When the drain runs out to t = 36 s (1 s + 5 s + 30 s, all three retries exhausted) —
            // each Advance waits for that retry's own Task.Delay timer to actually be created first...
            var run = drain.ProcessAsync(signal, CancellationToken.None);
            await time.WaitForTimerAsync(1, TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(1));
            await time.WaitForTimerAsync(2, TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(5));
            await time.WaitForTimerAsync(3, TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(30));
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            // ...then the guardian's own next pass runs, over the SAME lifecycle double. The grace
            // filter itself lives in the repository's SQL (Story357 pins it against real Postgres);
            // ClaimedPastGraceResult empty here is this fake answering exactly what that SQL would for
            // a row 36 s old, nowhere near the 6-minute grace.
            var guardian = new AnnouncementLifecycleGuardianService(
                lifecycle, time, NullLogger<AnnouncementLifecycleGuardianService>.Instance);
            await guardian.SweepOnceAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC6 — exactly one WARN with the id</summary>
        [Fact]
        public void WarnsOnceOnExhaustion() =>
            Assert.Single(provider.Messages, m => m.Contains(AnnouncementId.ToString(), StringComparison.Ordinal));

        /// <summary>AC7 — no re-arm inside the grace</summary>
        [Fact]
        public void NeverReArms() => Assert.Empty(lifecycle.ReArmCalls);

        /// <summary>Pins SPEC F202.2's own backoff: the delays the drain itself asked the clock for, not how far the test advanced it.</summary>
        [Fact]
        public void RetriesWaitOneFiveThenThirtySeconds() =>
            Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)], time.DueTimes);
    }
}
