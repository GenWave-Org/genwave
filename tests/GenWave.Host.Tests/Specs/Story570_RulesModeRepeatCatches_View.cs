// STORY-570 — One flow for flagged talk, the repeat view (SPEC F220.10 · PLAN T800, db/55)
//
// BDD specification — xUnit. Rules mode writes no repeat-caught booth rows, so station.v_repeat_catches_7d also
// counts the talk_check rows whose repeat-phrases verdict was on and flagged. The Postgres-backed facts are
// Category=Integration (a fresh install: db/01 + db/06).

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureRulesModeRepeatCatchesView
{
    const string Flagged =
        """{"on":true,"flag":{"reason":"Repeated: banger, log it","ask":"Do not say: banger, log it","evidence":"banger, log it"}}""";

    const string NotFlagged = """{"on":true,"flag":null}""";

    const string SwitchedOff =
        """{"on":false,"flag":{"reason":"Repeated: banger, log it","ask":"Do not say: banger, log it","evidence":"banger, log it"}}""";

    const string CouldNotCheck = """{"on":true,"flag":{"reason":"Could not check","ask":"Write it again, keeping to every rule.","evidence":""}}""";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    // db/55 and the fresh-init mirror
    public sealed class ScenarioDb06AndDb55CarryTheSameView
    {
        [Fact]
        public void TheViewTextIsIdentical()
        {
            var root = RepoRootLocator.Find(AppContext.BaseDirectory);
            var fresh = MirroredSqlBlock.Read(Path.Combine(root, "db", "06-station-settings-migration.sh"), "f220-repeat-catches");
            var upgrade = MirroredSqlBlock.Read(Path.Combine(root, "db", "55-repeat-catches-rules-migration.sh"), "f220-repeat-catches");

            Assert.Equal(fresh, upgrade);
        }
    }

    // A rules-mode repeat catch counts beside the legacy booth rows
    [Trait("Category", "Integration")]
    public sealed class ScenarioARulesModeRepeatCatchCounts : IAsyncLifetime
    {
        RepeatViewsDatabase? database;
        (int Catches, int Trims, int Fallbacks) numbers;

        // Given one DJ with: 1 repeat-caught booth row (legacy), and talk_check rows of
        //   - mode rules, repeat-phrases on and flagged            (counts)
        //   - mode rules, repeat-phrases on and flagged, 3 days ago (counts)
        //   - mode rules, repeat-phrases on and not flagged        (does not)
        //   - mode rules, repeat-phrases switched off              (does not)
        //   - mode rules, the judge could not run                  (does not)
        //   - mode rules, no repeat-phrases verdict at all         (does not)
        //   - mode shadow, flagged                                 (does not: shadow rows never counted)
        //   - mode rules, flagged, 8 days ago                      (does not: outside 7 days)
        // When  the repeat view is queried
        public async Task InitializeAsync()
        {
            database = await RepeatViewsDatabase.StartAsync();
            var flip = await database.AddPersonaAsync("Flip");
            await database.AddRepeatCaughtAsync(flip, "banger, log it", "template");
            await database.AddTalkCheckAsync(flip, "rules", Flagged);
            await database.AddTalkCheckAsync(flip, "rules", Flagged, TimeSpan.FromDays(3));
            await database.AddTalkCheckAsync(flip, "rules", NotFlagged);
            await database.AddTalkCheckAsync(flip, "rules", SwitchedOff);
            await database.AddTalkCheckAsync(flip, "rules", CouldNotCheck);
            await database.AddTalkCheckAsync(flip, "rules", null);
            await database.AddTalkCheckAsync(flip, "shadow", Flagged);
            await database.AddTalkCheckAsync(flip, "rules", Flagged, TimeSpan.FromDays(8));
            numbers = await database.RepeatCatchesAsync(flip);
        }

        public async Task DisposeAsync()
        {
            if (database is not null) await database.DisposeAsync();
        }

        [Fact]
        public void ItCountsTheBoothRowAndTheTwoFlaggedRulesRows() => Assert.Equal(3, numbers.Catches);

        [Fact]
        public void RulesRowsAreNeverTrims() => Assert.Equal(0, numbers.Trims);
    }
}
