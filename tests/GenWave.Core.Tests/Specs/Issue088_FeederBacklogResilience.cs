// gh-#88 — playout: feeder refill-per-advance death spiral + pick/ownership lifetime tied to ring,
// not air.
//
// BDD specification — xUnit. Adopts the hunter's repro (found chasing the T73 stamp regression,
// 2026-07-22 dev stack) for two coupled PlayoutFeeder defects, plus a review-round-2 finding on
// the fix itself:
//   Defect 1 — the refill trigger (PlayoutFeeder.cs) recognized only the CURRENT chain as "ours";
//   any backlog beyond it (a metadata flap, or an api restart mid-chain) meant every advance read
//   as foreign, pushing a whole new chain per advance — an unbounded engine queue (345 requests
//   observed live). Fixed by keying the no-op on feederOwnedIds, not chainIds.
//   Defect 2 — pushedMeta/feederOwnedIds liveness (SPEC F57.1) ended at ring eviction, not at the
//   pushed item's own observed airing. Once air-lag exceeded RecentWindow, the F57.4 echo branch
//   mistook the late arrival for an engine-initiated play and nulled its PersonaPick — the T73
//   booth-log stamp silently lost. Fixed with claim (d): "pushed, air not yet PROVEN aired" — a
//   FIFO queue (pendingAirQueue), not a same-id-keyed set, so it survives both a TTS segment whose
//   own advance is never individually observed (proven aired instead when a later, observed sibling
//   further along in push order is caught — DrainPendingAirUpTo) and a genuinely abandoned chain
//   (proven only by a CONFIRMED drain — ReleasePriorChainPendingAir), with a live size cap as the
//   last-resort backstop for an id that never airs at all.
// Fix 1 requires fix 2: the no-op recognition on a backlog only holds if ownership (claim d)
// outlives ring retention long enough for a backlogged id to reach it. Claim (d) must in turn NOT
// strip on a mere foreign-but-real advance (review round 2): that only proves something ELSE,
// already queued ahead, aired — our own backlog is not abandoned, and ScenarioPickSurvivesAirLag-
// BeyondRingRetention below is exactly the case a too-eager strip would silently re-break.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFeederBacklogResilience
{
    sealed class RecordingSink : IStationEventSink
    {
        public List<TrackAired> Aired { get; } = [];
        public void Publish(StationEvent evt) { if (evt is TrackAired t) Aired.Add(t); }
    }

    static MediaItem Item(string id) =>
        new(id, $"/media/{id}.mp3", $"title-{id}", new Loudness(-16.0, -1.0, Measurable: true));

    static IReadOnlySet<string> Real(params string[] ids) => new HashSet<string>(ids);

    static FakeRotationSettingsProvider Rotation(int recentWindow) =>
        new(new RotationSettings { RecentWindow = recentWindow, ArtistSeparation = 0 });

    public sealed class ScenarioPickSurvivesAirLagBeyondRingRetention
    {
        [Fact]
        public async Task APushedPersonaPickIsStillAttachedWhenItsAirLagsBehindRingEviction()
        {
            // Small ring (capacity 2) forces fast eviction; live equivalent was RecentWindow=250
            // against ~2.5 pushes/min, ~100 min of air-lag before m1's original stamp went missing.
            var pick = new PersonaPickDiagnostics(
                PoolSize: 18, TopScores: [0.5], FiredRules: [], IsExploration: false);
            var m1 = Item("m1") with { PersonaPick = pick };

            // Tick 1: boot (safe) → refill pushes m1 (with its persona pick).
            // Tick 2: foreign "X" airs → refill pushes m2; ring [X, m2] evicts m1's ring slot.
            // Tick 3: foreign "Y" airs → refill pushes m3, keeping the spiral honest.
            // Tick 4: m1 finally reaches the front of the backlogged queue and airs.
            var ls = new FakeLiquidsoapControl(["safe", "X", "Y", "m1"], Real("X", "Y", "m1"));
            var provider = new FakeNextItemProvider(m1, Item("m2"), Item("m3"));
            var sink = new RecordingSink();
            var feeder = new PlayoutFeeder(ls, provider, Rotation(2), events: sink);

            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            // THE BUG: m1 was pushed with a persona pick, but by air time its ring slot had long
            // been evicted, so the F57.4 echo branch overwrote pushedMeta with PersonaPick:null and
            // TrackAired published null — booth_log.pick stamped NULL.
            var m1Aired = Assert.Single(sink.Aired, t => t.MediaId == "m1");
            Assert.Same(pick, m1Aired.PersonaPick);
        }
    }

    public sealed class ScenarioBackloggedQueueSelfDrains
    {
        [Fact]
        public async Task AnAdvanceOntoAFeederOwnedBacklogIdDoesNotTriggerAnotherChainPush()
        {
            // Once the engine queue holds anything beyond the CURRENT chain, every advance was onto
            // an id outside chainIds — the feeder pushed ANOTHER full chain per advance, so the
            // queue only ever grew (345 queued requests observed live). An advance onto an id this
            // feeder itself pushed — just not in the current chain — must be a no-op instead.
            var ls = new FakeLiquidsoapControl(
                ["safe", "X", "m1", "m2"],                 // X = one foreign seed, then OUR backlog airs
                Real("X", "m1", "m2"));
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"), Item("m3"), Item("m4"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(20));

            await feeder.TickAsync(CancellationToken.None);   // boot: push m1                  (queue: m1)
            await feeder.TickAsync(CancellationToken.None);   // foreign X seeds: push m2       (queue: m1, m2)
            await feeder.TickAsync(CancellationToken.None);   // m1 (ours) airs from backlog
            await feeder.TickAsync(CancellationToken.None);   // m2 (ours, chain end) airs

            // m1's advance lands on a feeder-pushed id with m2 still queued behind it — a no-op.
            // Only m2 (the chain end) reaching air earns a new chain: m1 (boot) + m2 (seed) + m3.
            Assert.Equal(3, ls.Pushed.Count);
        }
    }

    public sealed class ScenarioRepeatPushOfTheSameIdStaysExactlyOncePerAiring
    {
        [Fact]
        public async Task ATrackPushedTwiceAndAiredTwicePublishesTwoTrackAiredEventsWithTheirOwnPicks()
        {
            // The FIFO pendingAirQueue (claim d) must not collapse a legitimate repeat push of the
            // same id into a single airing: "m1" is selected twice, with two DIFFERENT persona
            // picks, separated by an unrelated "m2" airing (the only way this on-air-id abstraction
            // can represent two distinct occurrences of the same id). DrainPendingAirUpTo always
            // resolves the OLDEST still-pending occurrence first (nearest the queue's front), so the
            // second push's fresh entry is untouched by the first occurrence's drain.
            var pick1 = new PersonaPickDiagnostics(
                PoolSize: 10, TopScores: [0.9], FiredRules: [], IsExploration: false);
            var pick2 = new PersonaPickDiagnostics(
                PoolSize: 12, TopScores: [0.1], FiredRules: [], IsExploration: true);

            var ls = new FakeLiquidsoapControl(["safe", "m1", "m2", "m1"], Real("m1", "m2"));
            var provider = new FakeNextItemProvider(
                Item("m1") with { PersonaPick = pick1 },
                Item("m2"),
                Item("m1") with { PersonaPick = pick2 });
            var sink = new RecordingSink();
            var feeder = new PlayoutFeeder(ls, provider, Rotation(20), events: sink);

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes m1 (pick1)
            await feeder.TickAsync(CancellationToken.None);   // m1 airs (1st) → refill pushes m2
            await feeder.TickAsync(CancellationToken.None);   // m2 airs → refill re-pushes m1 (pick2)
            await feeder.TickAsync(CancellationToken.None);   // m1 airs (2nd)

            var m1Events = sink.Aired.Where(t => t.MediaId == "m1").ToList();
            Assert.Equal(2, m1Events.Count);
            Assert.Same(pick1, m1Events[0].PersonaPick);
            Assert.Same(pick2, m1Events[1].PersonaPick);
        }
    }

    public sealed class ScenarioUnobservedSubPollPushIsProvenByALaterSibling
    {
        // A TTS segment can air and depart entirely between two 3s polls (PlayoutFeederService's
        // tick interval) — its OWN advance is never individually caught. The naive "removed only on
        // its own observed advance" claim (the pre-review-round-2 design) would pin it forever; the
        // FIFO drain-up-to path releases it the moment the chain's LATER item (m1, individually
        // observed as chain-end) proves it aired too.
        static async Task<PlayoutFeeder> RunAsync(FakeLiquidsoapControl ls)
        {
            var ttsSeg = new MediaItem("tts:seg", "/media/tts-seg.wav", "Segment",
                new Loudness(-16.0, -1.0, Measurable: true), DurationMs: 4_000);
            var provider = new FakeNextItemProvider(ttsSeg, Item("m1"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(1));

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes chain [tts:seg, m1]
            await feeder.TickAsync(CancellationToken.None);   // m1 (chain end) airs — tts:seg's OWN advance was never seen
            await feeder.TickAsync(CancellationToken.None);   // tts:seg reappears — no live claim protects it

            return feeder;
        }

        static FakeLiquidsoapControl Ls() =>
            new(["safe", "m1", "tts:seg"], Real("m1", "tts:seg"));

        [Fact]
        public async Task ItsPushedMetaIsReleasedOnceTheChainsLaterItemProvesItAired()
        {
            var feeder = await RunAsync(Ls());

            // A retained feeder-authoritative entry would still report the pushed title; a released
            // one falls through to the engine-echo branch, which carries none (bare track_id only).
            Assert.Null(feeder.CurrentOnAir?.Title);
        }

        [Fact]
        public async Task ItsFeederOwnershipIsReleasedOnceTheChainsLaterItemProvesItAired()
        {
            var feeder = await RunAsync(Ls());

            // DurationMs never rides the engine annotate line (F50.2) — non-null is only reachable
            // via the feeder-owned push branch, so null here proves ownership was released too.
            Assert.Null(feeder.CurrentOnAir?.DurationMs);
        }
    }

    public sealed class ScenarioAbandonedChainReleasesOnlyOnAConfirmedDrain
    {
        [Fact]
        public async Task AWholeUnobservedChainReleasesOnceTheQueueIsConfirmedDrained()
        {
            // Neither tts:seg NOR m1 is ever individually observed on-air — the very next poll shows
            // "safe" again, a CONFIRMED drain (no track_id airing at all), proving the engine's queue
            // that held this chain is genuinely empty. That must release the whole abandoned chain —
            // the ReleasePriorChainPendingAir path, distinct from the later-sibling path above.
            var ttsSeg = new MediaItem("tts:seg", "/media/tts-seg.wav", "Segment",
                new Loudness(-16.0, -1.0, Measurable: true), DurationMs: 4_000);
            var ls = new FakeLiquidsoapControl(["safe", "safe", "tts:seg"], Real("tts:seg"));
            var provider = new FakeNextItemProvider(ttsSeg, Item("m1"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(1));

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes chain [tts:seg, m1]
            await feeder.TickAsync(CancellationToken.None);   // still "safe" — confirmed drain, chain abandoned
            await feeder.TickAsync(CancellationToken.None);   // tts:seg reappears — no live claim protects it

            Assert.Null(feeder.CurrentOnAir?.Title);
        }

        [Fact]
        public async Task AForeignButRealAdvanceDoesNotAbandonAStillQueuedChain()
        {
            // The negative case that guards against the review-round-2 near-miss: a foreign-but-real
            // id airing must NOT be mistaken for a confirmed drain. m1 stays queued behind "X" and
            // must still carry its feeder-pushed title when it eventually airs, proving claim (d)
            // was never wrongly stripped just because something else aired first.
            var m1 = new MediaItem("m1", "/media/m1.mp3", "Feeder Title",
                new Loudness(-16.0, -1.0, Measurable: true));
            var ls = new FakeLiquidsoapControl(["safe", "X", "m1"], Real("X", "m1"));
            var provider = new FakeNextItemProvider(m1, Item("m2"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(1));

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes m1
            await feeder.TickAsync(CancellationToken.None);   // foreign X airs (real, not a drain) → refill pushes m2
            await feeder.TickAsync(CancellationToken.None);   // m1 finally airs from the backlog

            Assert.Equal("Feeder Title", feeder.CurrentOnAir?.Title);
        }
    }

    public sealed class ScenarioMidChainNoOpDrainReleasesByproductPredecessors
    {
        // Review round 2's blocker: DrainPendingAirUpTo dequeued predecessors WITHOUT a ReleaseIfDead
        // check — unlike Remember's and MarkPendingAir's own eviction loops. A MID-CHAIN no-op advance
        // (an id this feeder owns airs, but it isn't the CURRENT chain's end) runs no chain-reset
        // sweep at all, so a predecessor drained purely as a byproduct of someone ELSE'S observed
        // advance — here, tts:a, dragged out of the queue only because m1 (from the SAME original
        // chain, now backlogged behind a later chain) is observed — must get its liveness re-checked
        // right there, or it is pinned forever once its ring slot (small RecentWindow) is long gone.
        static async Task<PlayoutFeeder> RunAsync(FakeLiquidsoapControl ls)
        {
            var ttsA = new MediaItem("tts:a", "/media/tts-a.wav", "Segment",
                new Loudness(-16.0, -1.0, Measurable: true), DurationMs: 4_000);
            var provider = new FakeNextItemProvider(ttsA, Item("m1"), Item("m2"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(1));

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes chain [tts:a, m1]
            await feeder.TickAsync(CancellationToken.None);   // foreign X airs (real) → refill pushes m2; tts:a/m1 stay backlogged
            await feeder.TickAsync(CancellationToken.None);   // m1 airs — a MID-CHAIN no-op (m2 is now chain-end) — drains tts:a as a byproduct
            await feeder.TickAsync(CancellationToken.None);   // tts:a reappears — no live claim should protect it

            return feeder;
        }

        static FakeLiquidsoapControl Ls() =>
            new(["safe", "X", "m1", "tts:a"], Real("X", "m1", "tts:a"));

        [Fact]
        public async Task ItsPushedMetaIsReleasedNotJustItsQueueSlot()
        {
            var feeder = await RunAsync(Ls());

            Assert.Null(feeder.CurrentOnAir?.Title);
        }

        [Fact]
        public async Task ItsFeederOwnershipIsReleasedNotJustItsQueueSlot()
        {
            var feeder = await RunAsync(Ls());

            Assert.Null(feeder.CurrentOnAir?.DurationMs);
        }
    }
}
