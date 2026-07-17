// STORY-134 — Rotation never drains a playable catalog (Epic V / SPEC F41.6, closes gitea-#210) —
// feeder-window half. The catalog-query half lives in
// MediaLibrary.Tests/Specs/Story134_RotationNeverDrainsCatalogQuery.cs.
//
// BDD specification — xUnit. Implemented V4 (2026-07-14): PlayoutFeeder reads the window size from
// IRotationSettingsProvider at ring-write time — the hardcoded recentCapacity ctor default is
// retired. Observed indirectly through the ordered-recent list the feeder hands to
// INextItemProvider.GetNextAsync (PlayoutContext.RecentMediaIds) — the ring itself has no public
// accessor, mirroring the existing PlayoutFeederTests idiom (Feeder_Advance_DetectedByStampedIdChange_NotRid).

using GenWave.Core.Domain;
using GenWave.Core.Playout;
using GenWave.Core.Tests.Fakes;

namespace GenWave.Core.Tests.Specs;

public static class FeatureFeederRecentWindowLive
{
    static MediaItem Item(string id) =>
        new(id, $"/media/{id}.mp3", $"title-{id}", new Loudness(-16.0, -1.0, Measurable: true));

    static IReadOnlySet<string> Real(params string[] ids) => new HashSet<string>(ids);

    public sealed class ScenarioTheRecentWindowIsLiveTunable
    {
        [Fact]
        public async Task TheRingCapacityComesFromTheProviderNotACtorConstant()
        {
            // Window=1: only the single most-recently-advanced id survives in the ring, proving the
            // feeder no longer defaults to the old hardcoded recentCapacity=20.
            var ls = new FakeLiquidsoapControl(["A", "B", "C"], Real("A", "B", "C"));
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"), Item("m3"), Item("m4"));
            var rotation = new FakeRotationSettingsProvider(new RotationSettings { RecentWindow = 1 });
            var feeder = new PlayoutFeeder(ls, provider, rotation);

            await feeder.TickAsync(CancellationToken.None);   // boot at A
            await feeder.TickAsync(CancellationToken.None);   // advance to B → remembers A, then B
            await feeder.TickAsync(CancellationToken.None);   // advance to C → remembers C

            Assert.Equal(["C"], provider.Calls[^1].RecentMediaIds);
        }

        [Fact]
        public async Task ShrinkingTheWindowTrimsTheRingOnTheNextWrite()
        {
            // Start with a window of 3 — two advances fit without eviction. An empty provider keeps
            // this scenario isolated to F41.6 (window live-tunability): a non-empty one would also
            // enter its pushed ids into the ring at push time (SPEC F57.3), which is Story154's
            // concern, not this one.
            var ls = new FakeLiquidsoapControl(["A", "B", "C"], Real("A", "B", "C"));
            var provider = new FakeNextItemProvider();
            var rotation = new FakeRotationSettingsProvider(new RotationSettings { RecentWindow = 3 });
            var feeder = new PlayoutFeeder(ls, provider, rotation);

            await feeder.TickAsync(CancellationToken.None);   // boot at A
            await feeder.TickAsync(CancellationToken.None);   // advance to B → ring [A, B]
            Assert.Equal(["A", "B"], provider.Calls[^1].RecentMediaIds);

            // The live edit: no re-construction, no restart — same provider instance, new value.
            rotation.Settings = rotation.Settings with { RecentWindow = 1 };

            await feeder.TickAsync(CancellationToken.None);   // advance to C → trims to just [C]
            Assert.Equal(["C"], provider.Calls[^1].RecentMediaIds);
        }

        [Fact]
        public async Task GrowingTheWindowRetainsExistingEntriesAndAdmitsMore()
        {
            // An empty provider keeps this scenario isolated to F41.6 (window live-tunability) — see
            // the shrink test above for why a non-empty one would conflate it with SPEC F57.3.
            var ls = new FakeLiquidsoapControl(["A", "B", "C"], Real("A", "B", "C"));
            var provider = new FakeNextItemProvider();
            var rotation = new FakeRotationSettingsProvider(new RotationSettings { RecentWindow = 1 });
            var feeder = new PlayoutFeeder(ls, provider, rotation);

            await feeder.TickAsync(CancellationToken.None);   // boot at A
            await feeder.TickAsync(CancellationToken.None);   // advance to B → window=1 keeps only [B]
            Assert.Equal(["B"], provider.Calls[^1].RecentMediaIds);

            // The live edit: grow the window — B is still in the ring (never evicted), and the next
            // advance is admitted alongside it rather than evicting B.
            rotation.Settings = rotation.Settings with { RecentWindow = 5 };

            await feeder.TickAsync(CancellationToken.None);   // advance to C → ring [B, C]
            Assert.Equal(["B", "C"], provider.Calls[^1].RecentMediaIds);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioAZeroWindowDisablesAntiRepeat
    {
        [Fact]
        public async Task AZeroWindowYieldsAnEmptyRecentSnapshot()
        {
            var ls = new FakeLiquidsoapControl(["A", "B"], Real("A", "B"));
            var provider = new FakeNextItemProvider(Item("m1"), Item("m2"));
            var rotation = new FakeRotationSettingsProvider(new RotationSettings { RecentWindow = 0 });
            var feeder = new PlayoutFeeder(ls, provider, rotation);

            await feeder.TickAsync(CancellationToken.None);   // boot at A
            await feeder.TickAsync(CancellationToken.None);   // advance to B → remembered then evicted immediately

            Assert.Empty(provider.Calls[^1].RecentMediaIds);
        }
    }
}
