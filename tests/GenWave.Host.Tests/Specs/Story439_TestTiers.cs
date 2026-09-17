// STORY-439 — The test tiers are named, pinned and guarded (SPEC F177 · F183.2 · PLAN T504)
//
// BDD specification — xUnit. AC1–AC3 drive TierTraitConventionGuard.FindViolations (Support) over
// hand-built sample types — private nested classes, so xunit v2 never discovers them as tests and
// nothing here ever starts a Kokoro. AC4–AC7 are text pins over ci.yml, CONTRIBUTING.md and the
// MediaLibrary guard (Story107's repo-root grep-assert idiom).
//
// Was red at plan time (the guard was a throwing skeleton, ci.yml said integration runs
// "on-demand on a dev box", CONTRIBUTING had no "Test tiers" table); T504 turned it green.

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

public static class FeatureTestTiersNamedPinnedAndGuarded
{
    static string RepoRoot => RepoRootLocator.Find(AppContext.BaseDirectory);
    static string ReadRepoFile(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot, .. parts]));

    // Sample consumers the guard must reject — never discovered by xunit v2 (non-public), which
    // is exactly why the analyzer's "test classes must be public" rule is silenced here.
#pragma warning disable xUnit1000
    sealed class KokoroConsumerWithoutTrait : IClassFixture<KokoroFixture>
    {
        public KokoroConsumerWithoutTrait(KokoroFixture fixture) => _ = fixture;

        [Fact]
        public void SomeFact() { }
    }

    sealed class FakeStackFixture;

    sealed class StackConsumerWithoutTrait : IClassFixture<FakeStackFixture>
    {
        public StackConsumerWithoutTrait(FakeStackFixture fixture) => _ = fixture;

        [Fact]
        public void SomeFact() { }
    }
#pragma warning restore xUnit1000

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheShippedAssemblyPasses
    {
        [Fact]
        public void ReportsZeroViolations()
        {
            var violations = TierTraitConventionGuard.FindViolations(typeof(KokoroFixture).Assembly.GetExportedTypes());

            Assert.Empty(violations);
        }
    }

    public sealed class ScenarioCiPinsTheTierOneFilter
    {
        readonly string ci = ReadRepoFile(".github", "workflows", "ci.yml");

        [Fact]
        public void TheTestStepFiltersOutIntegration() =>
            Assert.Contains("--filter \"Category!=Integration\"", ci, StringComparison.Ordinal);

        [Fact]
        public void NoLineSaysIntegrationRunsOnADevBox() =>
            Assert.DoesNotContain("on-demand on a dev box", ci, StringComparison.Ordinal);

        [Fact]
        public void OneLineNamesTierTwoNightly() =>
            Assert.Single(ci.Split('\n'), line => line.Contains("tier 2 (nightly)", StringComparison.Ordinal));
    }

    public sealed class ScenarioContributingStatesTheThreeTiers
    {
        readonly string table;

        public ScenarioContributingStatesTheThreeTiers()
        {
            var text = ReadRepoFile("CONTRIBUTING.md");
            var start = text.IndexOf("Test tiers", StringComparison.Ordinal);
            table = start < 0 ? "" : text[start..];
        }

        [Fact]
        public void NamesTheTierOneFilter() =>
            Assert.Contains("`Category!=Integration`", table, StringComparison.Ordinal);

        [Fact]
        public void NamesTheTierTwoFilter() =>
            Assert.Contains("`Category=Integration`", table, StringComparison.Ordinal);

        [Fact]
        public void NamesTheManualPrefix() =>
            Assert.Contains("`manual:`", table, StringComparison.Ordinal);
    }

    public sealed class ScenarioTheMediaLibraryGuardIsUntouched
    {
        // The committed baseline hash of IntegrationTraitConventionGuard.cs (2026-09-15) — T504 adds
        // a Host twin, it never widens the MediaLibrary guard's scope.
        const string BaselineSha256 = "b8382267f983fd617a9b9be13c708a92fa8715beb5b0fc8c9eff734666cc7552";

        [Fact]
        public void IsByteIdenticalToTheBaseline()
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, "tests", "GenWave.MediaLibrary.Tests", "IntegrationTraitConventionGuard.cs"));
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

            Assert.Equal(BaselineSha256, hash);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the guard rejects
    // ---------------------------------------------------------------------

    public sealed class ScenarioAKokoroConsumerWithoutTheTraitIsRejected
    {
        [Fact]
        public void ReportsThatClassByName()
        {
            var violations = TierTraitConventionGuard.FindViolations([typeof(KokoroConsumerWithoutTrait)]);

            Assert.Contains(violations, v => v.Contains(nameof(KokoroConsumerWithoutTrait), StringComparison.Ordinal));
        }
    }

    public sealed class ScenarioAStackFixtureConsumerWithoutTheTraitIsRejected
    {
        [Fact]
        public void ReportsThatClassByName()
        {
            var violations = TierTraitConventionGuard.FindViolations(
                [typeof(StackConsumerWithoutTrait)], stackFixtureTypes: [typeof(FakeStackFixture)]);

            Assert.Contains(violations, v => v.Contains(nameof(StackConsumerWithoutTrait), StringComparison.Ordinal));
        }
    }
}
