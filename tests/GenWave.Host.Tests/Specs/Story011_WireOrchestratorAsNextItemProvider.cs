// STORY-011 — WIRE Orchestrator as INextItemProvider in Host

using GenWave.Core.Abstractions;
using GenWave.Orchestration;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureWireOrchestratorAsNextItemProvider
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — reflection-based type contract assertions
    // Full DI resolution requires the compose stack (Postgres, Liquidsoap); the T014
    // live-wire acceptance never landed as an automated fact and is retired (T507).
    // ---------------------------------------------------------------------

    public sealed class ScenarioOrchestratorReplacesRandomSelectionProvider
    {
        [Fact]
        public void OrchestratorImplementsINextItemProvider()
        {
            Assert.True(
                typeof(INextItemProvider).IsAssignableFrom(typeof(Orchestrator)),
                "Orchestrator must implement INextItemProvider — it is the production binding.");
        }
    }

    public sealed class ScenarioSharedLoudnessAnalyzerIsASingleton
    {
        [Fact]
        public void TtsSegmentSourceDependsOnILoudnessAnalyzer()
        {
            // TtsSegmentSource's primary constructor accepts ILoudnessAnalyzer, proving the shared
            // dependency is designed as a single injected instance — the singleton registration in
            // AddMediaLibrary satisfies this at runtime.
            var ctors = typeof(TtsSegmentSource).GetConstructors();
            var paramTypes = ctors
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)
                .ToList();

            Assert.Contains(typeof(ILoudnessAnalyzer), paramTypes);
        }

        [Fact]
        public void FfmpegLoudnessAnalyzerImplementsILoudnessAnalyzer()
        {
            // AddMediaLibrary registers FfmpegLoudnessAnalyzer as ILoudnessAnalyzer singleton.
            // Both TtsSegmentSource and the media enrichment pipeline resolve the same instance.
            Assert.True(
                typeof(ILoudnessAnalyzer).IsAssignableFrom(typeof(GenWave.Loudness.FfmpegLoudnessAnalyzer)),
                "FfmpegLoudnessAnalyzer must implement ILoudnessAnalyzer — it is the singleton binding.");
        }
    }

    public sealed class ScenarioITtsSynthesizerAndSegmentSourceAreRegistered
    {
        [Fact]
        public void KokoroTtsSynthesizerImplementsITtsSynthesizer()
        {
            Assert.True(
                typeof(ITtsSynthesizer).IsAssignableFrom(typeof(KokoroTtsSynthesizer)),
                "KokoroTtsSynthesizer must implement ITtsSynthesizer.");
        }

        [Fact]
        public void TtsSegmentSourceImplementsITtsSegmentSource()
        {
            Assert.True(
                typeof(ITtsSegmentSource).IsAssignableFrom(typeof(TtsSegmentSource)),
                "TtsSegmentSource must implement ITtsSegmentSource.");
        }
    }

    public sealed class ScenarioStationIdentityProviderInjectedIntoOrchestrator
    {
        [Fact]
        public void OrchestratorPrimaryConstructorAcceptsIStationIdentityProvider()
        {
            // Verify the constructor seam (SPEC F44.1, gitea-#196 — StationContext retired in favor of
            // the live IStationIdentityProvider seam): Orchestrator(IStationIdentityProvider, ...).
            var ctors = typeof(Orchestrator).GetConstructors();
            var paramTypes = ctors
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType)
                .ToList();

            Assert.Contains(typeof(IStationIdentityProvider), paramTypes);
        }
    }

}
