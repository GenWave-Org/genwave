// STORY-253 — Station pronunciation rules: Tts:Pronunciations setting-shape validation
//
// BDD specification — xUnit (SPEC F97.1, F97.3). The settings API's shape guard accepts
// {pattern, word, ipa} objects — word/ipa optional, mirroring PronunciationRuleSet.Create's own
// degrade-not-throw posture toward a blank/missing word or ipa — and rejects a non-string
// word/ipa or a missing pattern. Rule-engine semantics themselves (including P1/P2's degrade
// behavior for a blank/close-paren ipa) are pinned in GenWave.Tts.Tests (Story252/Story253).

using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePronunciationsSettingShape
{
    public sealed class ScenarioTtsPronunciationsShapeValidation
    {
        readonly SettingValidator validator = new(new ConfigurationBuilder().Build());

        [Theory]
        [InlineData("""[{"pattern":"MacLeod","word":"MacLeod","ipa":"/məˈklaʊd/"}]""")]
        [InlineData("""[{"pattern":"MacLeod","ipa":"/məˈklaʊd/"}]""")]                    // word optional
        [InlineData("""[{"pattern":"MacLeod","word":null,"ipa":"/məˈklaʊd/"}]""")]        // null = absent
        [InlineData("""[{"PATTERN":"MacLeod","WORD":"MacLeod","IPA":"/x/"}]""")]          // case-insensitive names
        [InlineData("[]")]
        public void AcceptsValidShapes(string value)
        {
            Assert.Null(validator.Validate("Tts:Pronunciations", value));
        }

        [Theory]
        [InlineData("""[{"word":"MacLeod","ipa":"/x/"}]""")]                              // missing pattern
        [InlineData("""[{"pattern":"MacLeod","word":5,"ipa":"/x/"}]""")]
        [InlineData("""[{"pattern":"MacLeod","ipa":["/x/"]}]""")]
        [InlineData("""[{"pattern":"MacLeod","word":true,"ipa":"/x/"}]""")]
        [InlineData("not json")]
        [InlineData("""{"pattern":"MacLeod"}""")]                                         // object, not array
        public void RejectsAnInvalidShape(string value)
        {
            Assert.NotNull(validator.Validate("Tts:Pronunciations", value));
        }
    }
}
