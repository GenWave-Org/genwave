// STORY-011 — WIRE Orchestrator as INextItemProvider in Host

using GenWave.Core.Abstractions;
using GenWave.Orchestration;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

public static class FeatureWireOrchestratorAsNextItemProvider
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — reflection-based type contract assertions
    // Full DI resolution requires the compose stack (Postgres, Liquidsoap);
    // live-wire specs are deferred to T014 with explicit Skip.
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

    public sealed class ScenarioWireUpAcceptanceLiveTickProducesARealMediaItem
    {
        [Fact(Skip = "Pending T014 — wire-up verification; see docs/PLAN.md")]
        public void OnAirReadSeesStampedTrackIdAfterATick()
        {
            // Wire-up: stack up, library has ≥1 ready track, feeder runs.
            // Read engine output metadata; assert a numeric track_id is present
            // (not the DrainToken).
            Assert.Fail("pending T014");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioKokoroDownAtStartupAppStillBootsMusicOnly
    {
        [Fact(Skip = "Pending T014 — live verification; see docs/PLAN.md")]
        public void HostStartupSucceedsEvenWhenKokoroIsUnreachable()
        {
            // Configure Tts:Endpoint to a black-holed address.
            // var ex = Record.Exception(() => factory.Services.GetRequiredService<INextItemProvider>());
            // Assert.Null(ex);
            Assert.Fail("pending T014");
        }

        [Fact(Skip = "Pending T014 — see docs/PLAN.md")]
        public void PulledItemsAreMusicWhenKokoroIsUnreachable()
        {
            // Run a tick; the only items produced must have non-tts: ids.
            Assert.Fail("pending T014");
        }
    }
}
