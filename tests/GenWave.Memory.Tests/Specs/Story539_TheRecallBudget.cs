// STORY-539 — The recall budget (gh-#293 gh-#920 · SPEC F218 · PLAN T700)
//
// BDD specification — xUnit. AC4/AC5 only; the other ACs live in tests/GenWave.Tts.Tests/Specs/Story539_TheEightKFloor.cs.
// Every fact is pending at plan time (2026-11-08); the named task activates its facts, watches them fail,
// then makes them pass.

using GenWave.Core.Domain;
using GenWave.Memory.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureTheRecallBudget
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC4 — The recall budget
    // "When the prompt is built" is asserted on the MemoryPacket; the prompt block itself lands in T701.
    public sealed class ScenarioTheRecallBudget
    {
        readonly InMemoryPersonaMemory store = new(new FakeTimeProvider());
        readonly MemoryPacket packet;

        public ScenarioTheRecallBudget()
        {
            for (var i = 1; i <= 5; i++) store.Add(MemoryKinds.Lore, $"lore {i}");
            for (var i = 1; i <= 10; i++) store.Add(MemoryKinds.Episode, $"episode {i}", occurredAgo: TimeSpan.FromDays(11 - i));
            for (var i = 1; i <= 10; i++) store.Add(MemoryKinds.Life, $"life {i}");
            packet = new MemoryRecallSource(store).TakeAsync(1, MemoryBudget.Standard, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void ItCarries2Lore() => Assert.Equal(2, packet.Lore.Count);

        [Fact]
        public void ItCarries3EpisodesNewestFirst() =>
            Assert.Equal(["episode 10", "episode 9", "episode 8"], packet.Episodes.Select(e => e.Content));

        [Fact]
        public void ItCarries3LifeRows() => Assert.Equal(3, packet.Life.Count);

        [Fact]
        public void EachRecalledRowSAiredCountHasIncrementedBeforeRender() =>
            Assert.All(packet.Lore.Concat(packet.Episodes).Concat(packet.Life),
                m => Assert.Equal(1, store.Rows.Single(r => r.Id == m.Id).AiredCount));
    }

    // AC5 — Not twice in two days
    public sealed class ScenarioNotTwiceInTwoDays
    {
        readonly MemoryPacket packet;

        public ScenarioNotTwiceInTwoDays()
        {
            var store = new InMemoryPersonaMemory(new FakeTimeProvider());
            store.Add(MemoryKinds.Episode, "the recent one", occurredAgo: TimeSpan.FromDays(1), airedAgo: TimeSpan.FromHours(47));
            store.Add(MemoryKinds.Episode, "the older one", occurredAgo: TimeSpan.FromDays(2), airedAgo: TimeSpan.FromHours(49));
            packet = new MemoryRecallSource(store).TakeAsync(1, MemoryBudget.Standard, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void ThatEpisodeIsNotRecalled() => Assert.DoesNotContain("the recent one", packet.Episodes.Select(m => m.Content));

        [Fact]
        public void AnEpisodeAiredFortyNineHoursAgoIsRecalled() => Assert.Equal(["the older one"], packet.Episodes.Select(m => m.Content));
    }

    // T725 - lore comes back about once a shift; episodes and life keep 48 h
    public sealed class ScenarioLoreIsHeardAboutOnceAShift
    {
        readonly MemoryPacket packet;

        public ScenarioLoreIsHeardAboutOnceAShift()
        {
            var store = new InMemoryPersonaMemory(new FakeTimeProvider());
            store.Add(MemoryKinds.Lore, "lore 5h", airedAgo: TimeSpan.FromHours(5));
            store.Add(MemoryKinds.Lore, "lore 7h", airedAgo: TimeSpan.FromHours(7));
            store.Add(MemoryKinds.Life, "life 47h", airedAgo: TimeSpan.FromHours(47));
            packet = new MemoryRecallSource(store).TakeAsync(1, MemoryBudget.Standard, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Fact]
        public void LoreAiredFiveHoursAgoIsSkipped() => Assert.DoesNotContain("lore 5h", packet.Lore.Select(m => m.Content));

        [Fact]
        public void LoreAiredSevenHoursAgoIsRecalled() => Assert.Contains("lore 7h", packet.Lore.Select(m => m.Content));

        [Fact]
        public void ALifeRowAiredFortySevenHoursAgoIsStillSkipped() => Assert.Empty(packet.Life);
    }
}
