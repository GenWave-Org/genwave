// gh-#161 — Context-aware corrections: Tts:Corrections setting-shape validation
//
// BDD specification — xUnit (SPEC F68.5 extension). The settings API's shape guard must accept the
// new optional whenPrecededBy/whenFollowedBy context fields (strings, or JSON null, or absent) and
// still reject a non-string context — a typo'd shape should bounce at PUT time with a clear error,
// not silently degrade the whole rule set inside SpeechCorrectionProvider. The rule-engine
// semantics themselves are pinned in GenWave.Tts.Tests (Gh161_ContextAwareCorrections).

using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureContextCorrectionsSettingShape
{
    public sealed class ScenarioTtsCorrectionsShapeValidation
    {
        readonly SettingValidator validator = new(new ConfigurationBuilder().Build());

        [Theory]
        [InlineData("""[{"from":"MacLeod","to":"Muh-cloud"}]""")]                                  // pre-gh-#161 shape
        [InlineData("""[{"from":"wind","to":"wynd","whenFollowedBy":"down|up"}]""")]
        [InlineData("""[{"from":"record","to":"wreckerd","whenPrecededBy":"a|the|that"}]""")]
        [InlineData("""[{"from":"tear","to":"tair","whenPrecededBy":"to","whenFollowedBy":"through"}]""")]
        [InlineData("""[{"from":"wind","to":"wynd","whenFollowedBy":null}]""")]                    // null = unconditional
        [InlineData("""[{"FROM":"wind","TO":"wynd","WHENFOLLOWEDBY":"down"}]""")]                  // case-insensitive names
        [InlineData("[]")]
        public void AcceptsValidShapes(string value)
        {
            Assert.Null(validator.Validate("Tts:Corrections", value));
        }

        [Theory]
        [InlineData("""[{"from":"wind","to":"wynd","whenFollowedBy":5}]""")]
        [InlineData("""[{"from":"wind","to":"wynd","whenPrecededBy":["down"]}]""")]
        [InlineData("""[{"from":"wind","to":"wynd","whenFollowedBy":{"word":"down"}}]""")]
        [InlineData("""[{"from":"wind","to":"wynd","whenPrecededBy":true}]""")]
        public void RejectsANonStringContext(string value)
        {
            Assert.NotNull(validator.Validate("Tts:Corrections", value));
        }
    }
}
