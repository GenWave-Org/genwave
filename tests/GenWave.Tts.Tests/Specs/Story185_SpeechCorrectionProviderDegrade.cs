// STORY-185 — Corrections live from settings through one call site (degrade contract)
//
// BDD specification — xUnit (SPEC F68.5). Story185_CorrectionsLiveWiring (GenWave.Host.Tests)
// exercises the live-reload wiring end to end; this file covers SpeechCorrectionProvider's own
// "never throw, always degrade to Empty" contract at construction time — malformed operator data
// must never take the whole api down at DI construction (SpeechCorrectionProvider is a singleton
// built eagerly at startup).
//
// Regression fact (review finding): `Tts:Corrections` stored as the JSON array "[null]"
// deserializes its element to an actual null reference (System.Text.Json does not enforce
// SpeechCorrection's non-nullable From/To), which used to NRE inside SpeechCorrectionSet.Create's
// filter and escape SpeechCorrectionProvider.Build's catch(JsonException) — a NullReferenceException
// is not a JsonException. Fixed at both layers: Create now skips a null element the same way it
// already skips a blank From, and Build's catch is widened to Exception as a defense-in-depth net
// for any OTHER deserialization surprise. With the Create-layer fix in place, "[null]" no longer
// throws at all (so no WARN is logged for it specifically — a silent skip, exactly like a blank
// From) — that's what the fact below proves; the widened catch remains the safety net for
// malformed shapes the Create-layer fix does not cover.

using GenWave.Tts.Tests.Fakes;
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSpeechCorrectionProviderDegrade
{
    public sealed class ScenarioNullArrayElementInCorrectionsJson
    {
        readonly TestOptionsMonitor<TtsCorrectionsOptions> options =
            new(new TtsCorrectionsOptions { Corrections = "[null]" });
        readonly CapturingLogger<SpeechCorrectionProvider> logger = new();

        [Fact]
        public void ConstructionNeverThrows()
        {
            var exception = Record.Exception(() => new SpeechCorrectionProvider(options, logger));
            Assert.Null(exception);
        }

        [Fact]
        public void NoOperatorCorrectionApplies()
        {
            var provider = new SpeechCorrectionProvider(options, logger);
            var result = SpeechText.Normalize("A deep cut from MacLeod.", provider.Current);
            Assert.Equal("A deep cut from MacLeod.", result);
        }
    }
}
