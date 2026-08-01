// STORY-253 — Station pronunciation rules live from settings through one call site (degrade contract)
//
// BDD specification — xUnit (SPEC F97.3). Mirrors Story185_SpeechCorrectionProviderDegrade.cs's own
// contract for the station side of the F97.3 merge: malformed Tts:Pronunciations JSON must never
// take the whole api down at DI construction (PronunciationRuleProvider is a singleton built eagerly
// at startup) — degrade to PronunciationRuleSet.Empty instead.

using GenWave.Tts.Tests.Fakes;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePronunciationRuleProviderDegrade
{
    public sealed class ScenarioNullArrayElementInPronunciationsJson
    {
        readonly TestOptionsMonitor<TtsPronunciationsOptions> options =
            new(new TtsPronunciationsOptions { Pronunciations = "[null]" });
        readonly CapturingLogger<PronunciationRuleProvider> logger = new();

        [Fact]
        public void ConstructionNeverThrows()
        {
            var exception = Record.Exception(() => new PronunciationRuleProvider(options, logger));
            Assert.Null(exception);
        }

        [Fact]
        public void NoStationRuleApplies()
        {
            var provider = new PronunciationRuleProvider(options, logger);
            Assert.Empty(provider.Current.Match("Here is MacLeod."));
        }
    }

    public sealed class ScenarioMalformedJson
    {
        readonly TestOptionsMonitor<TtsPronunciationsOptions> options =
            new(new TtsPronunciationsOptions { Pronunciations = "not json" });
        readonly CapturingLogger<PronunciationRuleProvider> logger = new();

        [Fact]
        public void ConstructionNeverThrows()
        {
            var exception = Record.Exception(() => new PronunciationRuleProvider(options, logger));
            Assert.Null(exception);
        }
    }

    // T137 review finding (Medium): SettingValidator only guards Tts:Pronunciations' JSON SHAPE
    // (Story253_PronunciationsSettingShape) — it accepts a rule that PronunciationRuleSet.Create
    // will later drop for being USELESS (a missing/blank ipa here), stores it, and — before this
    // fix — logged nothing anywhere. T142's rule-HIT counters can never surface this either: a
    // dropped rule never reaches Match, so it never hits. SPEC F97.5 exists precisely because
    // "is my rule working?" was unanswerable in the field; a silent new drop repeats that mistake.
    public sealed class ScenarioARuleThatFailsToCompileIsWarnedAbout
    {
        readonly TestOptionsMonitor<TtsPronunciationsOptions> options =
            new(new TtsPronunciationsOptions { Pronunciations = """[{"pattern":"MacLeod","word":"MacLeod"}]""" });
        readonly CapturingLogger<PronunciationRuleProvider> logger = new();

        [Fact]
        public void OneWarningNamesHowManyRulesDroppedAgainstHowManyWereDeclared()
        {
            _ = new PronunciationRuleProvider(options, logger);

            var warning = Assert.Single(logger.Warnings);
            Assert.Contains("1", warning, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioEveryDeclaredRuleCompilesCleanly
    {
        readonly TestOptionsMonitor<TtsPronunciationsOptions> options =
            new(new TtsPronunciationsOptions
            {
                Pronunciations = """[{"pattern":"MacLeod","word":"MacLeod","ipa":"/məˈklaʊd/"}]""",
            });
        readonly CapturingLogger<PronunciationRuleProvider> logger = new();

        [Fact]
        public void NoWarningIsLoggedWhenNothingWasDropped()
        {
            _ = new PronunciationRuleProvider(options, logger);

            Assert.Empty(logger.Warnings);
        }
    }
}
