// STORY-456 — The speaker travels with the plan — the render branch (gh-#772 · SPEC F189.3, F189.6 · PLAN T525, T526)
//
// BDD specification — xUnit. AC2–AC5 drive TtsSegmentSource with a recording synthesizer and ambient caches that throw on refresh; AC12 drives
// the Tts SpeakerSnapshotSource with an unknown id.
//
// RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body — remove the Skip only in the
// task that makes it green. The builder comments name the arrange each scenario needs.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureSnapshotDrivesTheRender
{
    const string Pending = "pending: T526 — TtsSegmentSource.RenderAsync branches on SegmentRequest.Speaker (STORY-456)";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioARequestWithASnapshot
    {
        // Given: Speaker pace 1.2 and one pronunciation rule

        /// <summary>AC2 — the synthesizer received 1.2</summary>
        [Fact(Skip = Pending)]
        public void SendsTheSnapshotPace() => Assert.Fail(Pending);

        /// <summary>AC2 — the rule rewrote the copy</summary>
        [Fact(Skip = Pending)]
        public void AppliesTheSnapshotRule() => Assert.Fail(Pending);
    }

    public sealed class ScenarioARequestWithASnapshotAndThrowingCaches
    {
        // Given: ambient caches throw on refresh

        /// <summary>AC3 — the snapshot bypasses the caches</summary>
        [Fact(Skip = Pending)]
        public void RendersSuccessfully() => Assert.Fail(Pending);
    }

    public sealed class ScenarioTwoSnapshotsSameText
    {
        // Given: identical text, ContentHash differs

        /// <summary>AC4 — the cache key carries the snapshot</summary>
        [Fact(Skip = Pending)]
        public void SynthesizesTwice() => Assert.Fail(Pending);
    }

    public sealed class ScenarioARequestWithoutASnapshot
    {
        // Given: Speaker null, ambient caches at pace 0.9

        /// <summary>AC5 — today's path unchanged</summary>
        [Fact(Skip = Pending)]
        public void SendsTheAmbientPace() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH — segregated
    // ---------------------------------------------------------------------

    public sealed class ScenarioAnUnknownPersonaId : IAsyncLifetime
    {
        // Given: the snapshot source resolves an id that does not exist
        const long UnknownPersonaId = 4242;

        readonly CapturingLogger<SpeakerSnapshotSource> logger = new();
        SpeakerSnapshot snapshot = new(null, null, "", TtsPace.EngineDefault, [], [], "");

        public async Task InitializeAsync()
        {
            var identityProvider = new FakeStationIdentityProvider(new StationIdentity("genwave-1", "Test", "af_heart"));
            var cards = new FakePersonaCardByIdSource();
            var pronunciations = new PronunciationRuleProvider(
                new TestOptionsMonitor<TtsPronunciationsOptions>(new TtsPronunciationsOptions()),
                NullLogger<PronunciationRuleProvider>.Instance);
            var corrections = new SpeechCorrectionProvider(
                new TestOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions()),
                NullLogger<SpeechCorrectionProvider>.Instance);

            var source = new SpeakerSnapshotSource(cards, identityProvider, pronunciations, corrections, logger);
            snapshot = await source.ForPersonaAsync(UnknownPersonaId, CancellationToken.None);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>AC12 — the unknown id degrades to the station's own snapshot.</summary>
        [Fact]
        public void FallsBackToTheStationSnapshot()
        {
            Assert.Null(snapshot.PersonaId);
            Assert.Equal("af_heart", snapshot.Voice);
        }

        /// <summary>AC12 — exactly one WARN, naming the id.</summary>
        [Fact]
        public void LogsOneWarnNamingTheId() => Assert.Single(logger.Warnings, w => w.Contains("4242"));
    }
}
