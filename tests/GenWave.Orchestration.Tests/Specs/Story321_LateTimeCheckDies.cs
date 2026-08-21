// STORY-321 — A late time check dies quietly (gh-#469 · SPEC F124.4 · PLAN VQ-f, T269)
//
// BDD specification — xUnit. The incident's third symptom: a 22:00-armed TimeDate deferral drained
// behind the backlog and announced the hour ten minutes late — the F71.8 never-invent-the-time class.
// The expiry predicate lives beside the hold filter in TryDequeueDue.
//
// Contract sharpening at T269 build time (T267 review round-2 finding F8, recorded on
// SpeechDeferralQueue.TryDequeueDue's own remarks): the scaffold this file replaces said "Due
// suffices (now − Due)" — that is the NAIVE wall-clock-only formula, and it under-counts lateness by
// exactly whatever runtime is still queued ahead of the pull that would otherwise drain a piece. The
// real, built formula is air-time lateness — realNow + queuedAhead − Due — mirroring the exact
// reasoning Orchestrator.HoldSignOnPastQueuedTail's own NotBefore arithmetic already applies to a
// held SignOn one seam over: a piece has not truly gone stale on air merely because wall-clock passed
// its Due, it goes stale once the ALREADY-QUEUED audio ahead of it would have finished draining.
// ScenarioATimeDateDeferralDrainingLateIsDropped's happy-path fact below is written against that
// sharpened formula directly (a punctual-by-wall-clock TimeDate that is still expired once its queued
// tail is accounted for) — a naive now-minus-Due predicate would wrongly pass it.
//
// Facts 1/4/5/6/7 exercise SpeechDeferralQueue directly — the predicate's own home, no Orchestrator
// needed. Facts 2/3 exercise the full Orchestrator, proving the threading this predicate cannot prove
// on its own: the live Station:Imaging:TimeAnnouncementBudgetSeconds value actually reaches the drain
// (fact 3), and a drop actually produces the SPEC F124.4 WARN (fact 2). One assertion per Fact; happy
// first; sad segregated.
//
// Retargeted at PLAN T326 (SPEC F141.1): StationImagingSettings.TimeAnnouncementStaleMinutes (minutes,
// default 5) widened to TimeAnnouncementBudgetSeconds (seconds, default 420) — gh-#526's field data
// showed every real overrun landing just past the old 300s ceiling. TryDequeueDue's own signature is
// untouched (it always took a plain TimeSpan budget, never the raw minutes shape) — only the settings
// record's own field, and the numbers this file feeds it, changed.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureLateTimeCheckDiesQuietly
{
    static MediaReference MakeTrackRef(string id) => new(
        id, $"/media/{id}.mp3", $"Track {id}", new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static Orchestrator BuildOrchestrator(
        SpeechDeferralQueue queue, TimeProvider clock, FakeTtsSegmentSource tts,
        FakeStationImagingSettingsProvider imagingSettings, ILogger<Orchestrator>? logger = null)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        });
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var catalog = new FakeMediaCatalog(MakeTrackRef("t1"));
        var musicSelectionPolicy = new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance);

        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy, tts,
            new FakeActivePersonaAccessor(),
            logger ?? NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            queue, clock, new FakeBoundaryBiasProvider(TimeSpan.Zero),
            imagingSettings: imagingSettings);
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public static class ScenarioATimeDateDeferralDrainingLateIsDropped
    {
        [Fact]
        public static void A_TimeDate_more_than_the_budget_past_Due_is_removed_undrained()
        {
            // Given a TimeDate deferral due at 14:00, draining with only 2 minutes of REAL wall-clock
            // time elapsed but 4 minutes of runtime already queued ahead of this pass — air-time
            // lateness is 2+4=6 minutes, past the 5-minute budget, even though the NAIVE
            // wall-clock-only lateness (2 minutes) would not be. This is the sharpened-formula fact
            // the file header calls out: a now-minus-Due predicate would wrongly keep this pending.
            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            var clock = new FakeTimeProvider(due);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", due);
            clock.Advance(TimeSpan.FromMinutes(2));

            // When the due-drain filter runs
            var result = queue.TryDequeueDue(
                clock.GetUtcNow(),
                queuedAhead: TimeSpan.FromMinutes(4),
                timeDateStaleBudget: TimeSpan.FromMinutes(5));

            // Then it is not returned and no longer pending
            Assert.Empty(result);
            Assert.Null(queue.Peek(SpeechDeferralKind.TimeDate));
        }

        [Fact]
        public static async Task One_WARN_names_the_armed_hour_and_the_lateness()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var logger = new CapturingLogger<Orchestrator>();
            var imagingSettings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(
                    ClockAnchoredIdents: false, TimeAnnouncements: false, TimeAnnouncementBudgetSeconds: 300),
            };
            var orchestrator = BuildOrchestrator(queue, clock, tts, imagingSettings, logger);

            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", due);
            clock.Advance(TimeSpan.FromMinutes(6)); // 60s past the 300-second budget = 360s lateness

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Scoped to the TimeDate expiry's own message — a harness with no CachingScheduleResolver
            // wired also logs its own, unrelated "no schedule resolver" WARN on the first unit; that
            // one is not this fact's concern.
            var warning = Assert.Single(logger.Warnings, w => w.Contains("TimeDate", StringComparison.Ordinal));
            Assert.Contains("14:00", warning); // the armed hour
            // The label→value pairing, not just the bare numbers (round-3 review finding F2): 360s is
            // labeled as lateness PAST THE ARMED HOUR, and 300s (the budget) is separately labeled as
            // the budget it was judged against — the two must never read as the same number meaning two
            // different things.
            Assert.Contains("360s past its armed hour", warning);
            Assert.Contains("budget 300s", warning);
        }
    }

    public static class ScenarioTheBudgetIsLiveEditable
    {
        [Fact]
        public static async Task The_live_setting_value_applies_at_drain_time_without_restart()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var imagingSettings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(
                    ClockAnchoredIdents: false, TimeAnnouncements: false, TimeAnnouncementBudgetSeconds: 1800),
            };
            // ONE Orchestrator, constructed ONCE — the live-edit below happens on the SAME running
            // instance, never a fresh construction, which is exactly what "no restart" means here.
            var orchestrator = BuildOrchestrator(queue, clock, tts, imagingSettings);

            // Phase 1 — a wide 1800-second (30-minute) budget: a TimeDate draining 10 minutes late still airs.
            queue.Enqueue(
                SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour",
                new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));
            clock.Advance(TimeSpan.FromMinutes(10));
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            // A whole unit (TimeDate + music) is planned atomically and buffered on the first pull —
            // drain the buffered music too (Story302's own two-call idiom) so the NEXT enqueue below
            // genuinely starts a fresh planning pass rather than replaying this unit's buffer.
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Phase 2 — the operator narrows the SAME live provider to 60 seconds (1 minute). A fresh
            // TimeDate, now 3 minutes late, is dropped on the very next drain — no Orchestrator
            // reconstruction.
            imagingSettings.Current = imagingSettings.Current with { TimeAnnouncementBudgetSeconds = 60 };
            queue.Enqueue(
                SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", clock.GetUtcNow());
            clock.Advance(TimeSpan.FromMinutes(3));
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            // Only phase 1's TimeDate ever reached air — phase 2's was dropped under the narrowed,
            // LIVE-edited budget the SAME running Orchestrator instance picked up with no restart.
            Assert.Single(tts.Requests, r => r.Kind == SegmentKind.TimeDate);
        }

        [Fact]
        public static void The_shipped_default_is_four_hundred_twenty_seconds()
        {
            // The domain record's own default IS the shipped SPEC F141.1 budget — pinned directly,
            // the Story151_SeededDefaults.cs "pinned against the options-class initializer" idiom
            // applied one project layer down (GenWave.Host.Options.StationImagingOptions' own
            // TimeAnnouncementBudgetSeconds mirrors this SAME 420, seeded verbatim into appsettings.json).
            var defaults = new StationImagingSettings(ClockAnchoredIdents: false, TimeAnnouncements: false);

            Assert.Equal(420, defaults.TimeAnnouncementBudgetSeconds);
        }
    }

    public static class ScenarioIdentsAreExemptByDesign
    {
        [Fact]
        public static void An_equally_late_StationId_deferral_drains_normally()
        {
            // A late ident is fine; a late time check invents the time (F124.4).
            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            var clock = new FakeTimeProvider(due);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.StationId, "cadence: Station:Cadence:StationIdEveryNUnits", due);
            clock.Advance(TimeSpan.FromMinutes(6)); // as late as the expired TimeDate fact above

            var result = queue.TryDequeueDue(
                clock.GetUtcNow(), timeDateStaleBudget: TimeSpan.FromMinutes(5));

            Assert.Single(result, d => d.Kind == SpeechDeferralKind.StationId);
        }
    }

    // SPEC F141.2 (STORY-355, PLAN T326, review round-1 finding F2) — the 90-second honesty threshold
    // itself: a survivor of the expiry check above (still inside the live budget) is classified a
    // SECOND time, on-time vs. late, and that classification is what Orchestrator stamps onto the
    // SegmentRequest it Kicks (SegmentRequest.TimeDateFreshness) — PatterTemplateRenderer's own
    // Story355 facts only ever prove the renderer's ternary reads that stamp correctly; nothing before
    // this scenario proved the THRESHOLD itself is ever reached through the real drain path. Both
    // facts share a wide (300s) budget so neither drain is anywhere near expiry — only the 90-second
    // honesty line is under test here.
    public static class ScenarioTheHonestyThresholdClassifiesEachDrain
    {
        [Fact]
        public static async Task Drained_80s_past_Due_stamps_OnTime()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var imagingSettings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(
                    ClockAnchoredIdents: false, TimeAnnouncements: false, TimeAnnouncementBudgetSeconds: 300),
            };
            var orchestrator = BuildOrchestrator(queue, clock, tts, imagingSettings);

            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", due);
            clock.Advance(TimeSpan.FromSeconds(80)); // inside the 90s honesty threshold, well inside the 300s budget

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.TimeDate);
            Assert.Equal(TimeAnnouncementFreshness.OnTime, request.TimeDateFreshness);
        }

        [Fact]
        public static async Task Drained_100s_past_Due_stamps_Late()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var imagingSettings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(
                    ClockAnchoredIdents: false, TimeAnnouncements: false, TimeAnnouncementBudgetSeconds: 300),
            };
            var orchestrator = BuildOrchestrator(queue, clock, tts, imagingSettings);

            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", due);
            clock.Advance(TimeSpan.FromSeconds(100)); // past the 90s honesty threshold, well inside the 300s budget

            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.TimeDate);
            Assert.Equal(TimeAnnouncementFreshness.Late, request.TimeDateFreshness);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioExpiryNeverBlocksTheNextHoursArm
    {
        [Fact]
        public static void EnqueueIfAbsent_re_arms_the_coming_hour_after_an_expiry_drop()
        {
            // Expiry only ever DROPS — the T230-F1 keep-alive is preserved; a dropped
            // 14:00 deferral never shadows the 15:00 arm.
            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            var clock = new FakeTimeProvider(due);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", due);
            clock.Advance(TimeSpan.FromMinutes(6));
            queue.TryDequeueDue(clock.GetUtcNow(), timeDateStaleBudget: TimeSpan.FromMinutes(5));
            Assert.Null(queue.Peek(SpeechDeferralKind.TimeDate)); // sanity: the 14:00 arm is gone

            var nextHour = new DateTimeOffset(2026, 8, 8, 15, 0, 0, TimeSpan.Zero);
            var armed = queue.EnqueueIfAbsent(
                SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", nextHour);

            Assert.True(armed);
        }

        [Fact]
        public static void A_TimeDate_within_the_budget_still_drains()
        {
            // The expiry threshold is exclusive of the ordinary drain window — a deferral landing
            // EXACTLY at the budget (not past it) is untouched, the normal case.
            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            var clock = new FakeTimeProvider(due);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", due);
            clock.Advance(TimeSpan.FromMinutes(5)); // exactly at the budget, not past it

            var result = queue.TryDequeueDue(
                clock.GetUtcNow(), timeDateStaleBudget: TimeSpan.FromMinutes(5));

            Assert.Single(result, d => d.Kind == SpeechDeferralKind.TimeDate);
        }
    }
}
