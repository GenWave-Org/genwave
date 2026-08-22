// gh-#612 — playout: the feeder cannot tell "pushed" from "will ever air".
//
// BDD specification — xUnit. Liquidsoap's q.push allocates the RID BEFORE resolving the URI, so a
// push of a nonexistent path returns a success-shaped numeric reply and dies engine-side at
// severity 3 with neither "error" nor "fail" in the line — invisible to the api and to every log
// sweep. On the 2026-08-22 dev-box incident (the gh-#610 root cause) that shape ran silently for
// SEVEN DAYS: ~261 dead pushes/24h, the safe rotation covering every hole. Two remedies land here:
//   Remedy 1 — a Host-side guard may now DECLINE a push (ILiquidsoapControl.PushAsync returns
//   null): the feeder must treat that as "nothing was pushed" — no ownership, no claim (d), no
//   chain membership — while still nudging the anti-repeat ring past the dead row so the very next
//   selection does not re-crown the same unplayable winner.
//   Remedy 2 — the feeder itself arms a PushLoss diagnostic when the safe rotation covers
//   SafeCoverTicksBeforePushLossSignal CONSECUTIVE observe ticks while its own pushes sit
//   unproven; the Host shell turns the signal into the WARN this failure class never had.

using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFeederPushHonesty
{
    static MediaItem Item(string id) =>
        new(id, $"/media/{id}.mp3", $"title-{id}", new Loudness(-16.0, -1.0, Measurable: true));

    static IReadOnlySet<string> Real(params string[] ids) => new HashSet<string>(ids);

    static FakeRotationSettingsProvider Rotation(int recentWindow) =>
        new(new RotationSettings { RecentWindow = recentWindow, ArtistSeparation = 0 });

    public sealed class ScenarioDeclinedPushEndsChainWithoutOwnership
    {
        readonly FakeLiquidsoapControl ls;
        readonly FakeNextItemProvider provider;
        readonly PlayoutFeeder feeder;

        public ScenarioDeclinedPushEndsChainWithoutOwnership()
        {
            // Two ticks of drain; m1's push is declined by the (faked) guard, m2's is accepted.
            ls = new FakeLiquidsoapControl(["safe", "safe"], Real());
            ls.DeclinePushIds.Add("m1");
            provider = new FakeNextItemProvider(Item("m1"), Item("m2"));
            feeder = new PlayoutFeeder(ls, provider, Rotation(2));
        }

        [Fact]
        public async Task ADeclinedPushReachesTheEngineNeverAndTheNextTickSelectsOn()
        {
            await feeder.TickAsync(CancellationToken.None);
            Assert.Empty(ls.Pushed);                          // declined — nothing reached the engine

            await feeder.TickAsync(CancellationToken.None);
            var pushed = Assert.Single(ls.Pushed);            // the retry tick moved on to m2
            Assert.Equal("m2", pushed.MediaId);
        }

        [Fact]
        public async Task TheDeclinedIdJoinsTheAntiRepeatRingSoSelectionMovesPastIt()
        {
            await feeder.TickAsync(CancellationToken.None);
            await feeder.TickAsync(CancellationToken.None);

            // Without the ring nudge the second pull's context carries no memory of m1 and the
            // selection seam is free to re-crown the exact row whose push just died — the tight
            // pick/decline loop remedy 1 exists to prevent.
            Assert.Contains("m1", provider.Calls[1].RecentMediaIds);
        }
    }

    public sealed class ScenarioPersistentSafeCoverArmsThePushLossSignal
    {
        [Fact]
        public async Task TheSignalArmsOnlyOnceTheSafeCoverOutlastsTheBlipThreshold()
        {
            // The engine never airs anything we push: boot tick pushes m1, then the unchanged
            // drain token covers tick after tick while each retry refill pushes the next item.
            var ls = new FakeLiquidsoapControl(["safe", "safe", "safe", "safe"], Real());
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"), Item("m3"), Item("m4"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(2));

            await feeder.TickAsync(CancellationToken.None);   // boot: push m1
            await feeder.TickAsync(CancellationToken.None);   // safe ×1: strip m1, push m2
            await feeder.TickAsync(CancellationToken.None);   // safe ×2: strip m2, push m3
            Assert.Null(feeder.PushLoss);                     // still inside the blip window

            await feeder.TickAsync(CancellationToken.None);   // safe ×3: the threshold tick
            var loss = feeder.PushLoss;
            Assert.NotNull(loss);

            // Each confirmed-drain refill strips the abandoned chain, so the pending push at the
            // threshold tick is the CURRENT retry chain's — m3, pushed by the tick before.
            Assert.Equal("m3", loss.OldestPendingId);
            Assert.Equal("title-m3", loss.Title);
            Assert.Equal(1, loss.PendingCount);
        }

        [Fact]
        public void TheBlipThresholdIsPinnedAtThreeTicks()
        {
            // ~9s at the 3s poll: outlasts a legitimate single-poll underrun blip (the gitea-#155
            // class) while still arming within the first safe-track airing of a real dead push.
            Assert.Equal(3, PlayoutFeeder.SafeCoverTicksBeforePushLossSignal);
        }
    }

    public sealed class ScenarioARealTrackReachingAirEndsTheEpisode
    {
        [Fact]
        public async Task TheSignalClearsTheMomentARealTrackAirs()
        {
            // Same dead-push run as above, but the tick-4 retry chain (m4) finally airs on tick 5.
            var ls = new FakeLiquidsoapControl(["safe", "safe", "safe", "safe", "m4"], Real("m4"));
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"), Item("m3"), Item("m4"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(2));

            for (var tick = 0; tick < 4; tick++) await feeder.TickAsync(CancellationToken.None);
            Assert.NotNull(feeder.PushLoss);                  // armed while the drain persisted

            await feeder.TickAsync(CancellationToken.None);   // m4 reaches air
            Assert.Null(feeder.PushLoss);
        }
    }

    public sealed class ScenarioLegitimateQuietStatesNeverSignal
    {
        [Fact]
        public async Task ABriefSafeBlipBetweenRealTracksStaysBelowTheThreshold()
        {
            // m1 airs, a two-tick safe blip follows (advance-to-safe + one unchanged tick), then
            // m2 airs — the gitea-#155 shape the threshold exists to tolerate.
            var ls = new FakeLiquidsoapControl(["safe", "m1", "safe", "safe", "m2"], Real("m1", "m2"));
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"), Item("m3"));
            var feeder = new PlayoutFeeder(ls, provider, Rotation(2));

            for (var tick = 0; tick < 5; tick++)
            {
                await feeder.TickAsync(CancellationToken.None);
                Assert.Null(feeder.PushLoss);
            }
        }

        [Fact]
        public async Task ABootDrainWithNothingPushedIsHonestNotLost()
        {
            // An empty selection seam (no playable rows yet) under a persistent drain: nothing was
            // pushed, so nothing is unproven — however long the safe rotation covers, no signal.
            var ls = new FakeLiquidsoapControl(["safe", "safe", "safe", "safe", "safe"], Real());
            var provider = new FakeNextItemProvider();
            var feeder = new PlayoutFeeder(ls, provider, Rotation(2));

            for (var tick = 0; tick < 5; tick++)
            {
                await feeder.TickAsync(CancellationToken.None);
                Assert.Null(feeder.PushLoss);
            }
        }
    }
}
