// STORY-456 — The speaker travels with the plan (gh-#772 · SPEC F189.1 · PLAN T524) — additive
// contract half.
//
// BDD specification — xUnit. SpeakerSnapshot and SpeechCorrection move into GenWave.Abstractions;
// SegmentRequest.Speaker is a defaulted body property, not a 15th primary-constructor parameter
// (the CrosstalkAiredThisBreak/TimeDateFreshness precedent SegmentRequest's own remarks describe),
// so every existing SegmentRequest construction site compiles unchanged (AC1). The render half
// lives in Tts.Tests (Story456_SnapshotDrivesTheRender); card-by-id is Host.Tests
// (Story456_PersonaCardById); the planner/ceremony half is Orchestration.Tests
// (Story456_SpeakerTravelsWithThePlan, pending T527); the seam index is Architecture.Tests
// (Story456_SeamIndex, pending T528).

using GenWave.Core.Domain;

namespace GenWave.Core.Tests.Specs;

public static class FeatureAdditiveContract
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSegmentRequestBuiltWithTodaysArguments
    {
        readonly SegmentRequest request;

        public ScenarioSegmentRequestBuiltWithTodaysArguments()
        {
            // Given: a SegmentRequest built with exactly today's 6 required positional arguments
            request = new SegmentRequest(
                Kind: SegmentKind.StationId,
                Voice: "af_heart",
                StationName: "GenWave",
                Track: null,
                LocalNow: DateTimeOffset.UtcNow,
                StationId: "station-1");
        }

        /// <summary>AC1 — Speaker is null.</summary>
        [Fact]
        public void SpeakerIsNull()
        {
            Assert.Null(request.Speaker);
        }
    }

    public sealed class ScenarioSpeakerSnapshotSetThroughAnObjectInitializer
    {
        readonly SpeakerSnapshot speaker;
        readonly SegmentRequest request;

        public ScenarioSpeakerSnapshotSetThroughAnObjectInitializer()
        {
            // Given: a SpeakerSnapshot, set on a SegmentRequest through an object initializer —
            // never a positional/named constructor argument (the published-arity precedent).
            speaker = new SpeakerSnapshot(
                PersonaId: 7,
                PersonaName: "DJ Nova",
                Voice: "af_heart",
                Pace: 1.05,
                Rules: [],
                Corrections: [new SpeechCorrection("MacLeod", "muh-CLOUD")],
                ContentHash: "abc123");

            request = new SegmentRequest(
                Kind: SegmentKind.StationId,
                Voice: "af_heart",
                StationName: "GenWave",
                Track: null,
                LocalNow: DateTimeOffset.UtcNow,
                StationId: "station-1")
            {
                Speaker = speaker,
            };
        }

        [Fact]
        public void SpeakerSetThroughTheInitializerReadsBack()
        {
            Assert.Same(speaker, request.Speaker);
        }
    }

    public sealed class ScenarioPublishedArityStaysPinned
    {
        /// <summary>
        /// The published-NuGet-arity guard <see cref="SegmentRequest"/>'s own remarks describe: a
        /// 15th primary-constructor parameter would delete the shipped 14-arg ctor and Deconstruct
        /// from the compiled binary surface, breaking every compiled caller regardless of the new
        /// parameter's default value.
        /// </summary>
        [Fact]
        public void SegmentRequestsPrimaryConstructorHasFourteenParameters()
        {
            var ctor = typeof(SegmentRequest).GetConstructors().Single();

            Assert.Equal(14, ctor.GetParameters().Length);
        }
    }
}
