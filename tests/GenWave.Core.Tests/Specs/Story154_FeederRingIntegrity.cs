// STORY-154 — The feeder ring tells the truth and never double-airs (Epic Z / SPEC F57,
// closes gitea-#219, gitea-#220, gitea-#229).
//
// BDD specification — xUnit. Implemented Z1 (2026-07-15): one invariant, not three patches —
// pushedMeta/feederOwnedIds lifetime decouples from the anti-repeat ring (an entry lives while its
// id is on-air ∨ in the pushed chain ∨ in any ring slot — bare-id eviction retired, F57.1);
// feeder-pushed items join the ring AT PUSH TIME, engine-initiated plays at first observed advance,
// each airing remembered exactly once (F57.3). Idiom mirrors Story134_RotationNeverDrains /
// Story146_FeederStampsDuration: PlayoutFeeder driven with fakes (ILiquidsoapControl,
// INextItemProvider, rotation-settings provider). pushedMeta/feederOwnedIds/the ring have no public
// accessor, so assertions read state indirectly — via CurrentOnAir and the
// PlayoutContext.RecentMediaIds snapshot handed to INextItemProvider on the next selection call —
// exactly the existing PlayoutFeederTests idiom.

using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFeederRingIntegrity
{
    // A measured item with a non-zero, non-round gain (min(target-integrated, ceiling-truePeak) =
    // min(2.234, 5.0) = 2.234) — distinct from the 0.0 an engine-echo overwrite would produce, since
    // the fakes below never stamp a replay_gain annotation.
    static MediaItem Item(string id, string title = "title", int durationMs = 100_000) =>
        new(id, $"/media/{id}.mp3", title, new Loudness(-18.234, -6.0, Measurable: true), DurationMs: durationMs);

    static IReadOnlySet<string> Real(params string[] ids) => new HashSet<string>(ids);

    static FakeRotationSettingsProvider Rotation(int recentWindow) =>
        new(new RotationSettings { RecentWindow = recentWindow });

    static FakeRotationSettingsProvider DefaultRotation() => Rotation(20);

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — metadata liveness: on-air ∨ queued ∨ in-ring
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioMetadataSurvivesRepeatOccupancy
    {
        // A 2-track catalog with a 2-slot ring: m1 airs, m2 airs, then m1 repeats — that third
        // Remember() evicts m1's OLDER ring slot while the freshly re-pushed (not-yet-aired) m1
        // occupies the chain, so the id never loses all three F57.1 liveness claims at once.
        // Observing the repeat airing (tick 4) proves the feeder-authoritative entry survived: an
        // engine-echo overwrite would null the title/duration and re-round the gain to 0.0, since
        // the fake's bare track_id-only annotate line carries neither a title nor a replay_gain.
        static async Task<PlayoutFeeder> RunAsync(FakeLiquidsoapControl ls)
        {
            var provider = new FakeNextItemProvider(
                Item("m1", "Track One", durationMs: 111_000),
                Item("m2", "Track Two", durationMs: 222_000),
                Item("m1", "Track One", durationMs: 111_000));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(2));

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes m1
            await feeder.TickAsync(CancellationToken.None);   // m1 airs (chain end) → pushes m2
            await feeder.TickAsync(CancellationToken.None);   // m2 airs (chain end) → repeat-pushes m1
            await feeder.TickAsync(CancellationToken.None);   // the repeat m1 airs

            return feeder;
        }

        static FakeLiquidsoapControl Ls() => new(["safe", "m1", "m2", "m1"], Real("m1", "m2"));

        [Fact]
        public async Task AnOlderOccurrenceLeavingTheRingKeepsTheEntryWhileANewerOccurrenceRemains()
        {
            var feeder = await RunAsync(Ls());

            // Had the older ring slot's eviction wiped pushedMeta, the repeat airing would report the
            // engine-echo default (no title on the annotate line) instead of the feeder-pushed one.
            Assert.Equal("Track One", feeder.CurrentOnAir?.Title);
        }

        [Fact]
        public async Task DurationMsStaysFeederAuthoritativeAcrossRepeatAirings()
        {
            var feeder = await RunAsync(Ls());

            Assert.Equal(111_000, feeder.CurrentOnAir?.DurationMs);
        }

        [Fact]
        public async Task GainDbNeverReRoundsToTwoDecimalsOnARepeatAiring()
        {
            var ls = Ls();
            var feeder = await RunAsync(ls);

            // The engine-echo path parses "X.XX dB" or defaults to 0.0 when the annotation is absent
            // (as it always is on this fake) — either way it would not match the full-precision gain
            // the feeder itself computed and pushed for the repeat occurrence.
            var repeatPushGain = ls.PushedGains[2];
            Assert.NotEqual(0.0, repeatPushGain);
            Assert.Equal(repeatPushGain, feeder.CurrentOnAir?.GainDb);
        }
    }

    public sealed class ScenarioMetadataIsReleasedWhenNoLivenessHolds
    {
        // Window=1 forces a fast eviction cadence. m1 airs, then an unrelated engine-initiated
        // advance ("other1") evicts m1's only ring slot at the very moment m1 is no longer on-air and
        // no longer chained — releasing it (F57.1). m1's later, independent reappearance then has no
        // feeder-authoritative entry left to protect it: the code takes the engine-initiated branch,
        // overwriting pushedMeta with the annotate-line defaults (no title, no duration).
        static async Task<PlayoutFeeder> RunAsync(FakeLiquidsoapControl ls)
        {
            var provider = new FakeNextItemProvider(Item("m1", "Once Alive", durationMs: 50_000));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(1));

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes m1
            await feeder.TickAsync(CancellationToken.None);   // m1 airs (chain end) → refill (provider exhausted)
            await feeder.TickAsync(CancellationToken.None);   // "other1" airs → evicts m1's ring slot → releases it
            await feeder.TickAsync(CancellationToken.None);   // m1 reappears — no liveness claim protects it

            return feeder;
        }

        static FakeLiquidsoapControl Ls() => new(["safe", "m1", "other1", "m1"], Real("m1", "other1"));

        [Fact]
        public async Task AnIdWithNoLivenessIsRemovedFromPushedMeta()
        {
            var feeder = await RunAsync(Ls());

            // Was "Once Alive" at the first airing; a released pushedMeta entry means the reappearance
            // is read fresh from the (title-less) engine annotate line instead.
            Assert.Null(feeder.CurrentOnAir?.Title);
        }

        [Fact]
        public async Task AnIdWithNoLivenessIsRemovedFromFeederOwnedIds()
        {
            var feeder = await RunAsync(Ls());

            // DurationMs never rides the annotate line (F50.2) — it can only be non-null via the
            // feeder-owned branch. A released feederOwnedIds entry means the reappearance takes the
            // engine-initiated branch, so DurationMs comes back null despite the original push's
            // 50_000.
            Assert.Null(feeder.CurrentOnAir?.DurationMs);
        }
    }

    public sealed class ScenarioWindowZeroKeepsTheAiringTrackAuthoritative
    {
        // RecentWindow=0: the ring is always empty. m1 airs, then two more STEADY-STATE polls (the
        // same "m1" reported again, no advance) exercise the "publish current on-air state" path
        // repeatedly with nothing to re-derive from except the cached pushedMeta entry — proving
        // window=0 never evicts it via the on-air liveness claim (F57.1(a)) alone.
        static async Task<PlayoutFeeder> RunAsync(FakeLiquidsoapControl ls)
        {
            var provider = new FakeNextItemProvider(Item("m1", "Zero Window Track", durationMs: 77_000));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(0));

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes m1
            await feeder.TickAsync(CancellationToken.None);   // m1 airs
            await feeder.TickAsync(CancellationToken.None);   // steady poll — m1 still airing
            await feeder.TickAsync(CancellationToken.None);   // another steady poll — m1 still airing

            return feeder;
        }

        static FakeLiquidsoapControl Ls() => new(["safe", "m1", "m1", "m1"], Real("m1"));

        [Fact]
        public async Task WindowZeroDoesNotEvictTheOnAirIdsMetadata()
        {
            var feeder = await RunAsync(Ls());

            Assert.Equal(77_000, feeder.CurrentOnAir?.DurationMs);
        }

        [Fact]
        public async Task SubsequentPollsUnderWindowZeroNeverEngineEchoAFeederPushedAiring()
        {
            var ls = Ls();
            var feeder = await RunAsync(ls);

            // An engine-echo overwrite would null the title and re-derive gain from the (absent)
            // replay_gain annotation, landing on 0.0.
            Assert.Equal("Zero Window Track", feeder.CurrentOnAir?.Title);
            Assert.Equal(ls.PushedGains[0], feeder.CurrentOnAir?.GainDb);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — remember-at-push (F57.3)
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioQueuedMeansRecent
    {
        [Fact]
        public async Task APushedIdIsInTheRecentSnapshotBeforeAnyAdvanceIsObserved()
        {
            // Drained: the chain is [tts:seg1, m1] — pulling m1 (the chain's SECOND pull, still
            // within the same tick) already sees tts:seg1 in the recent snapshot, even though onAirId
            // is still "safe" and tts:seg1's own advance has not yet been observed by any tick.
            var ls = new FakeLiquidsoapControl(["safe"], Real());
            var provider = new FakeNextItemProvider(Item("tts:seg1"), Item("m1"));
            var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);

            Assert.Equal(["tts:seg1"], provider.Calls[1].RecentMediaIds);
        }

        [Fact]
        public async Task ATrackWhoseAdvanceIsNeverObservedStillExcludesFromTheNextSelection()
        {
            // m1 is pushed then never observed on-air at all — the engine's next reported advance
            // jumps straight to a different real id ("other"). Even though no tick ever saw m1's own
            // advance, it must still exclude from the next selection (the gitea-#220 sub-poll defect).
            var ls = new FakeLiquidsoapControl(["safe", "other"], Real("other"));
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"));
            var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes m1
            await feeder.TickAsync(CancellationToken.None);   // "other" airs directly — m1's advance was never observed

            Assert.Contains("m1", provider.Calls[^1].RecentMediaIds);
        }

        [Fact]
        public async Task AnObservedAdvanceOfAPushRememberedIdDoesNotReEnqueueIt()
        {
            var ls = new FakeLiquidsoapControl(["safe", "m1"], Real("m1"));
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"));
            var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → pushes m1 (remembered at push)
            await feeder.TickAsync(CancellationToken.None);   // m1's advance IS observed — must not re-enqueue

            // A single slot, not two: a re-enqueue at advance would double m1's occupancy from one airing.
            Assert.Equal(["m1"], provider.Calls[^1].RecentMediaIds);
        }

        [Fact]
        public async Task AnEngineInitiatedPlayStillEntersTheRingAtFirstObservedAdvance()
        {
            // The feeder never pushes "engine1" (safe rotation / restart) — its only entry point is
            // the first observed advance, unchanged by F57.3 (which only moves the entry point
            // earlier, to push time, for FEEDER-pushed ids).
            var ls = new FakeLiquidsoapControl(["safe", "engine1"], Real("engine1"));
            var provider = new FakeNextItemProvider();
            var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);   // boot (safe) → nothing to push
            await feeder.TickAsync(CancellationToken.None);   // engine1 airs — first observed advance

            Assert.Equal(["engine1"], provider.Calls[^1].RecentMediaIds);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioShippedBehaviorsAreUntouched
    {
        [Fact]
        public async Task EngineInitiatedIdsStillReExtractMetadataOnEveryAdvance()
        {
            // "engine1" airs, leaves, then airs again — a second, independent engine-initiated
            // occurrence. The re-extract branch (F24.1/F29.9) must run on EVERY such occurrence, not
            // just the first: if it only ran once, this second airing would never re-enter the ring,
            // and it would appear only once below instead of twice.
            var ls = new FakeLiquidsoapControl(["safe", "engine1", "other", "engine1"], Real("engine1", "other"));
            var provider = new FakeNextItemProvider();
            var feeder = new PlayoutFeeder(ls, provider, DefaultRotation());

            await feeder.TickAsync(CancellationToken.None);   // boot (safe)
            await feeder.TickAsync(CancellationToken.None);   // engine1 airs (1st occurrence)
            await feeder.TickAsync(CancellationToken.None);   // other airs
            await feeder.TickAsync(CancellationToken.None);   // engine1 airs again (2nd occurrence)

            Assert.Equal(["engine1", "other", "engine1"], provider.Calls[^1].RecentMediaIds);
        }

        [Fact]
        public async Task ALiveWindowShrinkStillTrimsTheRingOnTheNextWrite()
        {
            // The F41.6 regression guard, reproduced against Story154's own harness per this story's
            // acceptance criteria — Story134_FeederRecentWindowLive covers the same invariant in depth.
            var ls = new FakeLiquidsoapControl(["A", "B", "C"], Real("A", "B", "C"));
            var provider = new FakeNextItemProvider();
            var rotation = Rotation(3);
            var feeder = new PlayoutFeeder(ls, provider, rotation);

            await feeder.TickAsync(CancellationToken.None);   // boot at A
            await feeder.TickAsync(CancellationToken.None);   // advance to B
            Assert.Equal(["A", "B"], provider.Calls[^1].RecentMediaIds);

            rotation.Settings = rotation.Settings with { RecentWindow = 1 };

            await feeder.TickAsync(CancellationToken.None);   // advance to C → trims to just [C]
            Assert.Equal(["C"], provider.Calls[^1].RecentMediaIds);
        }

        [Fact]
        public async Task AnEmptyRingNeverBlocksSelectionFromReturningACandidate()
        {
            // RecentWindow=0: the ring is always empty. A one-track catalog forces an immediate
            // repeat selection — the feeder must still push it unconditionally; the ring is advisory
            // context for selection, never a gate the feeder itself enforces (F1.3, never-silent).
            var ls = new FakeLiquidsoapControl(["safe", "m1"], Real("m1"));
            var provider = new FakeNextItemProvider(Item("m1"), Item("m1"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(0));

            await feeder.TickAsync(CancellationToken.None);   // drain → pushes m1
            await feeder.TickAsync(CancellationToken.None);   // m1 airs (chain end) → repeat-pushes m1 again

            Assert.Equal(2, ls.Pushed.Count);
            Assert.Equal("m1", ls.Pushed[1].MediaId);
        }
    }
}
