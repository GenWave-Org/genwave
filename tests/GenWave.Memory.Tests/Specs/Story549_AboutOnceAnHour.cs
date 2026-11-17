// STORY-549 — About once an hour (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. CallbackPlanner over the in-memory callback store, a FakeTimeProvider and a seeded draw.
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

using GenWave.Core.Domain;
using GenWave.Memory.Tests.Fakes;

namespace GenWave.Memory.Tests.Specs;

public static class FeatureAboutOnceAnHour
{
    static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    // AC1 — the gates (table: last used, last offer, draw)
    public sealed class ScenarioTheGates
    {
        // Given Memory:CallbacksPerHour is 1

        [Fact]
        public async Task NeverUsedNeverOfferedAndAPassingDrawIsACallbackBreak()
        {
            var h = new CallbackPlannerHarness(0.1);
            h.Seed();
            Assert.NotNull(await h.PlanAsync());
        }

        [Fact]
        public async Task AFailingDrawIsNotACallbackBreak()
        {
            var h = new CallbackPlannerHarness(0.3);
            h.Seed();
            Assert.Null(await h.PlanAsync());
            Assert.Empty(h.Store.Offers);
        }

        [Fact]
        public async Task Used44MinutesAgoIsNotACallbackBreak()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("sings in a choir", "episode"), 44 * Minute, used: true);
            h.Seed();
            Assert.Null(await h.PlanAsync());
        }

        [Fact]
        public async Task Used46MinutesAgoIsACallbackBreak()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("sings in a choir", "episode"), 46 * Minute, used: true);
            h.Seed();
            Assert.NotNull(await h.PlanAsync());
        }

        [Fact]
        public async Task AnyOffer9MinutesAgoIsNotACallbackBreak()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("sings in a choir", "episode"), 9 * Minute);
            h.Seed();
            Assert.Null(await h.PlanAsync());
        }

        [Fact]
        public async Task AnyOffer11MinutesAgoIsACallbackBreak()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("sings in a choir", "episode"), 11 * Minute);
            h.Seed();
            Assert.NotNull(await h.PlanAsync());
        }

        [Fact]
        public async Task OnlyLeadInAndBackAnnounceBreaksCanBeCallbackBreaks()
        {
            var h = new CallbackPlannerHarness();
            h.Seed();
            foreach (var kind in Enum.GetValues<SegmentKind>())
            {
                var ask = await h.PlanAsync(kind: kind);
                Assert.Equal(kind is SegmentKind.LeadIn or SegmentKind.BackAnnounce, ask is not null);
                if (ask is not null)
                    h.Time.Advance(3 * Hour);
            }
        }

        [Fact]
        public async Task AGatedOutBreakDoesNotSpendTheDraw()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("sings in a choir", "episode"), 9 * Minute);
            await h.PlanAsync();
            Assert.Equal(0, h.Draw.Draws);
        }
    }

    // AC2 — a higher rate shortens the wait
    public sealed class ScenarioAHigherRateShortensTheWait
    {
        // Given the setting is 3 and the last used offer was 16 minutes ago

        [Fact]
        public async Task TheBreakIsACallbackBreakAtRate3()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("sings in a choir", "episode"), 16 * Minute, used: true);
            h.Seed();
            Assert.NotNull(await h.PlanAsync(perHour: 3));
        }

        [Fact]
        public async Task ARateAboveSixCountsAsSix()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("sings in a choir", "episode"), 7 * Minute, used: true);
            h.Seed();
            Assert.Null(await h.PlanAsync(perHour: 60));
        }
    }

    // AC3 — least recently offered first
    public sealed class ScenarioLeastRecentlyOfferedFirst
    {
        // Given one never-offered memory and two offered 3 h and 5 h ago

        [Fact]
        public async Task TheNeverOfferedMemoryIsPicked()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("keeps a goat named Gertrude"), 3 * Hour);
            h.Store.SeedOffer(h.Seed("grows tomatoes by the porch"), 5 * Hour);
            var never = h.Seed("has a dog named Daisy");
            var ask = await h.PlanAsync();
            Assert.Equal(never.Id, ask?.Memory.Id);
        }

        [Fact]
        public async Task AmongNeverOfferedMemoriesTheNewestIsPicked()
        {
            var h = new CallbackPlannerHarness();
            h.Seed("keeps a goat named Gertrude", age: TimeSpan.FromDays(3));
            var newest = h.Seed("has a dog named Daisy", age: TimeSpan.FromDays(1));
            h.Seed("grows tomatoes by the porch", age: TimeSpan.FromDays(2));
            Assert.Equal(newest.Id, (await h.PlanAsync())?.Memory.Id);
        }

        [Fact]
        public async Task TheOlderAskIsPickedWhenAllWereOffered()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("keeps a goat named Gertrude"), 3 * Hour);
            var older = h.Seed("grows tomatoes by the porch");
            h.Store.SeedOffer(older, 5 * Hour);
            Assert.Equal(older.Id, (await h.PlanAsync())?.Memory.Id);
        }
    }

    // AC4 — rest starts on use
    public sealed class ScenarioRestStartsOnUse
    {
        // Given lore used 5 h ago and a life memory used 47 h ago

        [Fact]
        public async Task NeitherRestedMemoryIsPicked()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed("restored a tube radio", "lore"), 5 * Hour, used: true);
            h.Store.SeedOffer(h.Seed("has a dog named Daisy", "life"), 47 * Hour, used: true);
            Assert.Null(await h.PlanAsync());
            Assert.Equal(2, h.Store.Offers.Count);
        }

        [Fact]
        public async Task TheLoreMemoryIsEligibleAgainAt7Hours()
        {
            var h = new CallbackPlannerHarness();
            var lore = h.Seed("restored a tube radio", "lore");
            h.Store.SeedOffer(lore, 7 * Hour, used: true);
            Assert.Equal(lore.Id, (await h.PlanAsync())?.Memory.Id);
        }

        [Fact]
        public async Task AUsedSettleStartsTheWaitAndTheRest()
        {
            var h = new CallbackPlannerHarness();
            h.Seed();
            var ask = await h.PlanAsync() ?? throw new InvalidOperationException("no ask");
            await h.Planner.SettleAsync(ask, 99, "like Daisy chasin' her tail", false, CancellationToken.None);
            h.Time.Advance(11 * Minute);

            var history = await h.Store.LastOfferAsync(CallbackPlannerHarness.Persona, CancellationToken.None);
            Assert.Equal(11 * Minute, history.SinceLastUsed);
            Assert.Null(await h.PlanAsync());
        }
    }

    // AC5 — a missed ask rests two hours
    public sealed class ScenarioAMissedAskRestsTwoHours
    {
        // Given a memory asked 90 minutes ago and not used

        [Fact]
        public async Task TheMemoryAsked90MinutesAgoIsSkipped()
        {
            var h = new CallbackPlannerHarness();
            h.Store.SeedOffer(h.Seed(), 90 * Minute);
            Assert.Null(await h.PlanAsync());
        }

        [Fact]
        public async Task At3HoursItIsEligibleAgain()
        {
            var h = new CallbackPlannerHarness();
            var memory = h.Seed();
            h.Store.SeedOffer(memory, 3 * Hour);
            Assert.Equal(memory.Id, (await h.PlanAsync())?.Memory.Id);
        }
    }

    // AC6 — off switch (sad path)
    public sealed class ScenarioOffSwitch
    {
        // Given the setting is 0
        // When  100 breaks are planned

        [Fact]
        public async Task NoneOf100BreaksIsACallbackBreak()
        {
            var h = new CallbackPlannerHarness();
            h.Seed();
            for (var i = 0; i < 100; i++)
            {
                Assert.Null(await h.PlanAsync(perHour: 0));
                h.Time.Advance(3 * Hour);
            }

            Assert.Empty(h.Store.Offers);
        }
    }

    // AC7 — nothing to call back to (sad path)
    public sealed class ScenarioNothingToCallBackTo
    {
        // Given a DJ with no eligible memory

        [Fact]
        public async Task ItIsNotACallbackBreakAndNoOfferIsWritten()
        {
            var h = new CallbackPlannerHarness();
            Assert.Null(await h.PlanAsync());
            Assert.Empty(h.Store.Offers);
        }
    }

    // AC8 — drafts and forgotten rows never air (sad path)
    public sealed class ScenarioDraftsAndForgottenRowsNeverAir
    {
        [Fact]
        public async Task AnUnapprovedDraftIsNotPicked()
        {
            var h = new CallbackPlannerHarness();
            h.Store.Seed(CallbackPlannerHarness.Persona, "lore", "restored a tube radio", unapprovedDraft: true);
            Assert.Null(await h.PlanAsync());
            Assert.Empty(h.Store.Offers);
        }

        [Fact]
        public async Task AForgottenRowIsNotPicked()
        {
            var h = new CallbackPlannerHarness();
            h.Store.Seed(CallbackPlannerHarness.Persona, "life", "has a dog named Daisy", forgotten: true);
            Assert.Null(await h.PlanAsync());
            Assert.Empty(h.Store.Offers);
        }
    }

    // AC9 — a memory with no distinctive word (sad path)
    public sealed class ScenarioAMemoryWithNoDistinctiveWord
    {
        // Given Maxxie Volt whose only memory is "Maxxie is here"

        [Fact]
        public async Task NothingIsPickedForAMemoryWithNoDistinctiveWord()
        {
            var h = new CallbackPlannerHarness();
            h.Seed("Maxxie is here");
            Assert.Null(await h.PlanAsync(exclusions: "Maxxie Volt"));
            Assert.Empty(h.Store.Offers);
        }

        [Fact]
        public async Task ABreakFactExcludesAMemoryWhoseOnlyWordIsTheTrack()
        {
            var h = new CallbackPlannerHarness();
            h.Seed("Butterfly", age: TimeSpan.FromHours(1));
            var daisy = h.Seed("has a dog named Daisy", age: TimeSpan.FromDays(2));
            Assert.Equal(daisy.Id, (await h.PlanAsync(exclusions: ["Butterfly", "Rusty Strings"]))?.Memory.Id);
        }

        [Fact]
        public async Task TenNewerNeverAskedHandoverEpisodesDoNotBlockAnOlderMemoryWithADistinctiveWord()
        {
            var h = new CallbackPlannerHarness();
            for (var i = 0; i < 10; i++)
                h.Seed("handed over to Maxxie Volt", kind: "episode", age: TimeSpan.FromHours(1 + i));
            var daisy = h.Seed("has a dog named Daisy", age: TimeSpan.FromDays(30));
            Assert.Equal(daisy.Id, (await h.PlanAsync(exclusions: "Maxxie Volt"))?.Memory.Id);
        }
    }
}
