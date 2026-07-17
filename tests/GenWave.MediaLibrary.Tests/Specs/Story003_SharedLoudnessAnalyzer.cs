// STORY-003 — Shared loudness analyzer

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureSharedLoudnessAnalyzer
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnalyzerAbstractionLivesInCore
    {
        [Fact(Skip = "Pending T004 — see docs/PLAN.md")]
        public void ILoudnessAnalyzerTypeIsInCoreAbstractions()
        {
            // var type = typeof(GenWave.Core.Abstractions.ILoudnessAnalyzer);
            // Assert.Equal("GenWave.Core.Abstractions", type.Namespace);
            Assert.Fail("pending T004");
        }
    }

    public sealed class ScenarioFfmpegImplementationLivesInSharedProject
    {
        [Fact(Skip = "Pending T004 — see docs/PLAN.md")]
        public void ImplementationAssemblyIsNotMediaLibrary()
        {
            // var impl = typeof(FfmpegLoudnessAnalyzer);
            // Assert.NotEqual("GenWave.MediaLibrary", impl.Assembly.GetName().Name);
            // (must be GenWave.Loudness or another shared sibling.)
            Assert.Fail("pending T004");
        }

        [Fact(Skip = "Pending T004 — see docs/PLAN.md")]
        public void ImplementationProjectIsReferencedByTtsProject()
        {
            // var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            //     .Select(a => a.GetName().Name);
            // Assert.Contains("GenWave.Tts", loadedAssemblies);
            Assert.Fail("pending T004");
        }
    }

    public sealed class ScenarioExistingEnrichmentTestsRemainGreen
    {
        [Fact(Skip = "Pending T004 — see docs/PLAN.md — verified by running the existing suite")]
        public void EnrichmentIntegrationSuitePassesAfterTheMove()
        {
            // Wire-up: dotnet test tests/GenWave.MediaLibrary.Tests/ must remain green
            // after FfmpegLoudnessAnalyzer is relocated. This spec is a marker that the suite
            // is expected to remain green; the actual assertion is the green CI run.
            Assert.Fail("pending T004 — verified by the full suite");
        }
    }

    public sealed class ScenarioShortClipMeasuresThroughTheSharedAnalyzer
    {
        [Fact(Skip = "Pending T004 — see docs/PLAN.md")]
        public void ShortWavClipReturnsMeasurableLoudness()
        {
            // var clip = await TestMedia.WriteShortToneWav(seconds: 3);
            // var loudness = await analyzer.AnalyzeAsync(clip, ct);
            // Assert.True(loudness.Measurable);
            Assert.Fail("pending T004");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNearSilentClipIsFlaggedUnmeasurable
    {
        [Fact(Skip = "Pending T004 — see docs/PLAN.md")]
        public void MeasurableIsFalseForSilentClip()
        {
            // var silent = await TestMedia.WriteSilentWav(seconds: 3);
            // var loudness = await analyzer.AnalyzeAsync(silent, ct);
            // Assert.False(loudness.Measurable);
            Assert.Fail("pending T004");
        }
    }
}
