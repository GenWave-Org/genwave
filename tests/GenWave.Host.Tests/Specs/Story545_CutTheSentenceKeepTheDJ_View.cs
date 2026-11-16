// STORY-545 — Cut the sentence, keep the DJ (gh-#293 gh-#920 · SPEC F218.30–.38 · PLAN T708)
//
// BDD specification — xUnit. AC8 over real Postgres (Category=Integration): station.v_repeat_catches_7d.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCutTheSentenceKeepTheDJView
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // AC8 — trims are counted apart from fallbacks
    [Trait("Category", "Integration")]
    public sealed class ScenarioTrimsAreCountedApartFromFallbacks : IAsyncLifetime
    {
        RepeatViewsDatabase? database;
        (int Catches, int Trims, int Fallbacks) numbers;

        // Given 2 repeat-caught rows ending "trimmed" and 1 ending "template" for one DJ in 7 days
        // (the template outcome also aired its fallback line, as production does)
        // When  the repeat view is queried
        public async Task InitializeAsync()
        {
            database = await RepeatViewsDatabase.StartAsync();
            var flip = await database.AddPersonaAsync("Flip");
            await database.AddRepeatCaughtAsync(flip, "banger, log it", "trimmed");
            await database.AddRepeatCaughtAsync(flip, "that was a stone cold classic", "trimmed", TimeSpan.FromDays(3));
            await database.AddRepeatCaughtAsync(flip, "welcome back to the shift", "template");
            await database.AddUtteranceAsync(flip, "flip|1|600|2026-11-09", "Here is a stock line.", fallback: true);
            numbers = await database.RepeatCatchesAsync(flip);
        }

        public async Task DisposeAsync()
        {
            if (database is not null) await database.DisposeAsync();
        }

        [Fact]
        public void ItReportsTrims2AndFallbacks1()
        {
            Assert.Equal(2, numbers.Trims);
            Assert.Equal(1, numbers.Fallbacks);
            Assert.Equal(3, numbers.Catches);
        }
    }
}
