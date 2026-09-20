// STORY-458 — BreakRenderer turns slots into outcomes (gh-#401 · SPEC F191 · PLAN T534)
//
// BDD specification — xUnit. Every scenario drives BreakRenderer.RenderAsync with a hand-built plan, a
// recording tts fake and a fake clock — never through Orchestrator (that replay stays STORY-452's own,
// frozen).
// Sad path: null, throw, budget miss.

using System.Globalization;
using System.Reflection;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBreakRenderer
{
    static readonly DateTimeOffset Start = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
    static readonly StationIdentity Identity = new("station-1", "GenWave", "voice-station");
    static readonly HashSet<SpeechDeferralKind> NoHolds = [];

    static BreakContext MakeContext(DateTimeOffset now) =>
        new(1, null, null, null, new CadenceConfig(), Identity, now, null, NoHolds, TimeSpan.Zero);

    static SegmentRequest MakeRequest(DateTimeOffset now, SegmentKind kind = SegmentKind.StationId) =>
        new(kind, Identity.Voice, Identity.Name, null, now, Identity.Id);

    static PlannedSlot RenderSlot(int ordinal, SegmentRequest request) =>
        new(ordinal, request.Kind, new RenderSource(request), null, new SilentDrop(), ObserveDuration: true);

    static PlannedSlot ReadySlot(int ordinal, MediaItem item) =>
        new(ordinal, SegmentKind.BackAnnounce, new ReadySource(item), null, new SilentDrop(), ObserveDuration: false);

    static PlannedSlot VerbatimSlot(int ordinal, SegmentRequest request, SegmentCopy copy, bool allowFlavor, long announcementId) =>
        new(
            ordinal, SegmentKind.Announcement, new VerbatimSource(request, copy, allowFlavor),
            new Reservation(ReservationKind.Announcement, announcementId.ToString(CultureInfo.InvariantCulture)),
            new SilentDrop(), ObserveDuration: true);

    static BreakPlan MakePlan(DateTimeOffset now, TimeSpan budget, params PlannedSlot[] slots) =>
        new(1, MakeContext(now), budget, slots);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFourSlotPlan
    {
        // Given: four Render slots, one per SegmentKind so each render's stamped MediaId is distinct.

        static BreakPlan BuildPlan(FakeTimeProvider clock)
        {
            var now = clock.GetUtcNow();
            return MakePlan(
                now, TimeSpan.FromSeconds(5),
                RenderSlot(1, MakeRequest(now, SegmentKind.StationId)),
                RenderSlot(2, MakeRequest(now, SegmentKind.LeadIn)),
                RenderSlot(3, MakeRequest(now, SegmentKind.BackAnnounce)),
                RenderSlot(4, MakeRequest(now, SegmentKind.TimeDate)));
        }

        /// <summary>AC1 — one outcome per slot.</summary>
        [Fact]
        public async Task ReturnsFourOutcomes()
        {
            var clock = new FakeTimeProvider(Start);
            var renderer = new BreakRenderer(new FakeTtsSegmentSource(), clock);

            var outcomes = await renderer.RenderAsync(BuildPlan(clock), CancellationToken.None);

            Assert.Equal(4, outcomes.Count);
        }

        /// <summary>AC1 — outcomes come back in ordinal order, never completion order.</summary>
        [Fact]
        public async Task ReturnsThemInOrdinalOrder()
        {
            var clock = new FakeTimeProvider(Start);
            var renderer = new BreakRenderer(new FakeTtsSegmentSource(), clock);

            var outcomes = await renderer.RenderAsync(BuildPlan(clock), CancellationToken.None);

            var mediaIds = outcomes.Select(o => ((RenderedOutcome)o).Item.MediaId);
            Assert.Equal(["tts:stationid-1", "tts:leadin-2", "tts:backannounce-3", "tts:timedate-4"], mediaIds);
        }
    }

    public sealed class ScenarioThreeOneSecondRenders
    {
        // Given: a tts fake stamping the fake clock per call, three one-second renders.

        /// <summary>AC2 — all three timestamps equal the start: every kick happens before the first await.</summary>
        [Fact]
        public async Task KicksEveryRenderAtTheStart()
        {
            var clock = new FakeTimeProvider(Start);
            var now = clock.GetUtcNow();
            var tts = new TimestampRecordingTtsSegmentSource(clock);
            var plan = MakePlan(
                now, TimeSpan.FromSeconds(10),
                RenderSlot(1, MakeRequest(now, SegmentKind.StationId)),
                RenderSlot(2, MakeRequest(now, SegmentKind.LeadIn)),
                RenderSlot(3, MakeRequest(now, SegmentKind.BackAnnounce)));
            var renderer = new BreakRenderer(tts, clock);

            // When RenderAsync is called but not yet awaited...
            var render = renderer.RenderAsync(plan, CancellationToken.None);

            // Then every render already recorded its own call timestamp — all three at the start,
            // none delayed until an earlier one finished.
            Assert.Equal([now, now, now], tts.CallTimestamps);

            clock.Advance(TimeSpan.FromSeconds(1));
            await render;
        }
    }

    public sealed class ScenarioAReadySlot
    {
        // Given: a Ready slot carrying an already-playable item.

        static readonly MediaItem ReadyItem = new(
            "ready-1", "/media/ready-1.mp3", "Ready One", new Loudness(-23.0, -1.0, true));

        /// <summary>AC3 — the item passes through as Rendered, untouched.</summary>
        [Fact]
        public async Task PassesTheItemThrough()
        {
            var clock = new FakeTimeProvider(Start);
            var plan = MakePlan(clock.GetUtcNow(), TimeSpan.FromSeconds(5), ReadySlot(1, ReadyItem));
            var renderer = new BreakRenderer(new FakeTtsSegmentSource(), clock);

            var outcomes = await renderer.RenderAsync(plan, CancellationToken.None);

            Assert.Equal([new RenderedOutcome(ReadyItem)], outcomes);
        }

        /// <summary>AC3 — a Ready slot never touches the tts seam at all.</summary>
        [Fact]
        public async Task MakesNoTtsCall()
        {
            var clock = new FakeTimeProvider(Start);
            var tts = new FakeTtsSegmentSource();
            var plan = MakePlan(clock.GetUtcNow(), TimeSpan.FromSeconds(5), ReadySlot(1, ReadyItem));
            var renderer = new BreakRenderer(tts, clock);

            await renderer.RenderAsync(plan, CancellationToken.None);

            Assert.Equal(0, tts.RenderCallCount);
        }
    }

    public sealed class ScenarioAVerbatimSlotWithFlavoredCopy
    {
        // Given: AllowFlavor=true and the announcement copy writer returns flavored text.

        /// <summary>AC4 — the flavored copy renders, not the plain fallback.</summary>
        [Fact]
        public async Task RendersTheFlavoredCopy()
        {
            var clock = new FakeTimeProvider(Start);
            var now = clock.GetUtcNow();
            var copyWriter = new FakeAnnouncementCopyWriter { Reply = "flavored copy" };
            var verbatimRenderer = new FakeVerbatimSegmentRenderer();
            var copy = new SegmentCopy("plain copy", FreshPerAiring: false);
            var plan = MakePlan(
                now, TimeSpan.FromSeconds(5),
                VerbatimSlot(1, MakeRequest(now, SegmentKind.Announcement), copy, allowFlavor: true, announcementId: 42));
            var renderer = new BreakRenderer(new FakeTtsSegmentSource(), clock, verbatimRenderer, copyWriter);

            await renderer.RenderAsync(plan, CancellationToken.None);

            Assert.Equal("flavored copy", Assert.Single(verbatimRenderer.Calls).Copy.Text);
        }
    }

    public sealed class ScenarioAVerbatimSlotWithoutFlavoredCopy
    {
        // Given: AllowFlavor=true but the announcement copy writer returns null.

        /// <summary>AC5 — the plain copy renders (SPEC F144.4's fallback law).</summary>
        [Fact]
        public async Task RendersThePlainCopy()
        {
            var clock = new FakeTimeProvider(Start);
            var now = clock.GetUtcNow();
            var copyWriter = new FakeAnnouncementCopyWriter(); // Reply defaults to null
            var verbatimRenderer = new FakeVerbatimSegmentRenderer();
            var copy = new SegmentCopy("plain copy", FreshPerAiring: false);
            var plan = MakePlan(
                now, TimeSpan.FromSeconds(5),
                VerbatimSlot(1, MakeRequest(now, SegmentKind.Announcement), copy, allowFlavor: true, announcementId: 7));
            var renderer = new BreakRenderer(new FakeTtsSegmentSource(), clock, verbatimRenderer, copyWriter);

            await renderer.RenderAsync(plan, CancellationToken.None);

            Assert.Equal("plain copy", Assert.Single(verbatimRenderer.Calls).Copy.Text);
        }
    }

    public sealed class ScenarioBreakRenderersConstructor
    {
        // Given: BreakRenderer's own, single constructor — AC6 proven architecturally rather than by
        // a capturing-fake run (Story455_BreakPlanner.NoFakeWasCalled's own precedent): no injectable
        // ILogger/IStationEventSink/IPatterDurationEstimator seam exists on it at all, a guarantee
        // that fails the instant such a seam is added rather than only when a scripted run happens to
        // reach it (SPEC F191.5's "no logger/event/estimator dependency").

        static readonly ParameterInfo[] ConstructorParameters =
            typeof(BreakRenderer).GetConstructors().Single().GetParameters();

        /// <summary>AC6 — no ILogger seam exists to hear anything.</summary>
        [Fact]
        public void DeclaresNoLoggerSeam() =>
            Assert.DoesNotContain(ConstructorParameters, p => typeof(ILogger).IsAssignableFrom(p.ParameterType));

        /// <summary>AC6 — no IStationEventSink seam exists to hear anything.</summary>
        [Fact]
        public void DeclaresNoEventSinkSeam() =>
            Assert.DoesNotContain(ConstructorParameters, p => typeof(IStationEventSink).IsAssignableFrom(p.ParameterType));

        /// <summary>AC6 — no IPatterDurationEstimator seam exists to hear anything.</summary>
        [Fact]
        public void DeclaresNoEstimatorSeam() =>
            Assert.DoesNotContain(ConstructorParameters, p => typeof(IPatterDurationEstimator).IsAssignableFrom(p.ParameterType));
    }

    public sealed class ScenarioARenderThatLandsOnTheBudget
    {
        // Given: a render that completes at exactly the budget.

        /// <summary>AC10 — outcome read from the task, not from which race member WhenAny reports as the winner.</summary>
        [Fact]
        public async Task IsRendered()
        {
            var clock = new FakeTimeProvider(Start);
            var budget = TimeSpan.FromSeconds(5);
            var tts = new FakeTtsSegmentSource { RenderDelay = budget, TimeProvider = clock };
            var plan = MakePlan(clock.GetUtcNow(), budget, RenderSlot(1, MakeRequest(clock.GetUtcNow())));
            var renderer = new BreakRenderer(tts, clock);

            var pending = renderer.RenderAsync(plan, CancellationToken.None);
            clock.Advance(budget);
            var outcomes = await pending;

            Assert.IsType<RenderedOutcome>(Assert.Single(outcomes));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — segregated
    // ---------------------------------------------------------------------

    public sealed class ScenarioANullRender
    {
        // Given: the tts fake returns null.

        /// <summary>AC7 — a null render fails without fabricating an exception.</summary>
        [Fact]
        public async Task IsFailed()
        {
            var clock = new FakeTimeProvider(Start);
            var tts = new FakeTtsSegmentSource { AlwaysReturnNull = true };
            var plan = MakePlan(clock.GetUtcNow(), TimeSpan.FromSeconds(5), RenderSlot(1, MakeRequest(clock.GetUtcNow())));
            var renderer = new BreakRenderer(tts, clock);

            var outcomes = await renderer.RenderAsync(plan, CancellationToken.None);

            Assert.Equal(new FailedOutcome(), Assert.Single(outcomes));
        }
    }

    public sealed class ScenarioAThrowingRender
    {
        // Given: the tts fake throws.

        /// <summary>AC8 — a throwing render fails too.</summary>
        [Fact]
        public async Task IsFailed()
        {
            var clock = new FakeTimeProvider(Start);
            var tts = new FakeTtsSegmentSource { ShouldThrow = _ => true };
            var plan = MakePlan(clock.GetUtcNow(), TimeSpan.FromSeconds(5), RenderSlot(1, MakeRequest(clock.GetUtcNow())));
            var renderer = new BreakRenderer(tts, clock);

            var outcomes = await renderer.RenderAsync(plan, CancellationToken.None);

            Assert.IsType<FailedOutcome>(Assert.Single(outcomes));
        }

        /// <summary>AC8 — the exception is carried, never swallowed silently.</summary>
        [Fact]
        public async Task CarriesTheException()
        {
            var clock = new FakeTimeProvider(Start);
            var tts = new FakeTtsSegmentSource { ShouldThrow = _ => true };
            var plan = MakePlan(clock.GetUtcNow(), TimeSpan.FromSeconds(5), RenderSlot(1, MakeRequest(clock.GetUtcNow())));
            var renderer = new BreakRenderer(tts, clock);

            var outcomes = await renderer.RenderAsync(plan, CancellationToken.None);

            Assert.IsType<InvalidOperationException>(((FailedOutcome)outcomes[0]).Exception);
        }
    }

    public sealed class ScenarioABudgetMiss
    {
        // Given: a 5 s budget, the render completes only after 6 s of fake time.

        /// <summary>AC9 — a render that misses the budget times out.</summary>
        [Fact]
        public async Task IsTimedOut()
        {
            var clock = new FakeTimeProvider(Start);
            var budget = TimeSpan.FromSeconds(5);
            var tts = new FakeTtsSegmentSource { RenderDelay = TimeSpan.FromSeconds(6), TimeProvider = clock };
            var plan = MakePlan(clock.GetUtcNow(), budget, RenderSlot(1, MakeRequest(clock.GetUtcNow())));
            var renderer = new BreakRenderer(tts, clock);

            var pending = renderer.RenderAsync(plan, CancellationToken.None);
            clock.Advance(budget);
            var outcomes = await pending;

            Assert.IsType<TimedOutOutcome>(Assert.Single(outcomes));
        }
    }
}

// A recording ITtsSegmentSource that stamps the fake clock at the START of each call, before its own
// one-second delay await — proves AC2's "every kick happens before the first await" without needing
// FakeTtsSegmentSource itself to grow a timestamp seam only this one spec needs.
file sealed class TimestampRecordingTtsSegmentSource(TimeProvider clock) : ITtsSegmentSource
{
    public List<DateTimeOffset> CallTimestamps { get; } = [];

    public async Task<MediaItem?> RenderAsync(SegmentRequest request, CancellationToken ct)
    {
        CallTimestamps.Add(clock.GetUtcNow());
        await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);

        var mediaId = $"tts:{CallTimestamps.Count}";
        return new MediaItem(mediaId, $"/tts/{mediaId}.wav", $"[{request.Kind}]", new Loudness(-23.0, -1.0, true));
    }
}
