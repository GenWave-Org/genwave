// STORY-388 — The Ad kind's settings-validation ruling (SPEC F158.3, PLAN T396, T390 review carry-
// forward 3)
//
// BDD specification — xUnit. SettingValidator.IsValidEngineByKindMap stays GENERIC (document-accept):
// a Tts:EngineByKind entry keyed "Ad" is valid JSON shape (a real SegmentKind name, a real engine
// name) even though it never applies — GenWave.Tts.TtsEngineByKindProvider is the one place that
// actually rejects it (see FeaturePerKindEngineOverride.ScenarioAdKindIsRejected in
// GenWave.Tts.Tests/Specs/Story191_PerKindEngineOverride.cs for that half of the ruling). This file
// pins the settings-validation half only — no live stack or DB required, the Story100 in-process
// pattern.

using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdKindSettingsRuling
{
    static SettingValidator BuildValidator() =>
        new(new ConfigurationBuilder().Build());

    public sealed class ScenarioDocumentAccept
    {
        [Fact]
        public void AnAdKeyedEngineByKindEntryValidatesGreen()
        {
            var error = BuildValidator().Validate("Tts:EngineByKind", """{"Ad":"piper"}""");

            Assert.Null(error);
        }
    }
}
