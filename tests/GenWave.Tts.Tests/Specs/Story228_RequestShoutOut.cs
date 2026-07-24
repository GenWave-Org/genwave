// STORY-228 — The DJ tips their hat, in their own words only (SPEC F87.7, PLAN T91)
//
// BDD specification — xUnit. Prompt-assembly and template facts: request color exists, listener
// text structurally cannot. Two of the five facts PLAN T91 names are Orchestration-side rather than
// here (Tts.Tests has no ProjectReference to GenWave.Orchestration, and never should just to prove
// this): "the shout-out rides the fulfilled track's own lead-in" and "cadence-off means no orphan"
// both depend on driving the real Orchestrator, so they live in
// GenWave.Orchestration.Tests/Specs/Story228_RequestShoutOut.cs instead.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;

public static class FeatureRequestShoutOut
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    static readonly DateTimeOffset FixedLocalNow = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    const string StationClockLine = "Current date/time (station-local): irrelevant";

    static MediaItem PlainTrack(bool requestFulfilled) =>
        new("m1", "/media/x.mp3", "Sundown", default, "Nite Owl", RequestFulfilled: requestFulfilled);

    static SegmentRequest LeadInRequest(bool requestFulfilled) =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave", PlainTrack(requestFulfilled), FixedLocalNow, "test-station");

    public static class ScenarioGenericAcknowledgment
    {
        [Fact]
        public static void AFulfilledPickAddsRequestLineColorToTheLlmLeadInPrompt()
        {
            var flaggedContent = LlmPromptBuilder.BuildUserContent(
                LeadInRequest(requestFulfilled: true), StationClockLine, previouslyVoicedTasteNotes: []);
            var unflaggedContent = LlmPromptBuilder.BuildUserContent(
                LeadInRequest(requestFulfilled: false), StationClockLine, previouslyVoicedTasteNotes: []);

            // Then the constant request-color instruction is present only when the pick was fulfilled
            // (SPEC F87.7) — never a mandate tied to any listener text, since none exists on this type.
            Assert.Contains("request line", flaggedContent);
            Assert.DoesNotContain("request line", unflaggedContent);
        }

        [Fact]
        public static void TheTemplateFallbackLeadInAlsoCarriesTheGenericAcknowledgment()
        {
            var renderer = new PatterTemplateRenderer();

            var flaggedCopy = renderer.Expand(LeadInRequest(requestFulfilled: true));
            var unflaggedCopy = renderer.Expand(LeadInRequest(requestFulfilled: false));

            // Then the template variant leads with the same generic acknowledgment family, station-
            // known metadata only (SPEC F87.7) — present only when flagged.
            Assert.Equal("Got this one in from the request line: Sundown by Nite Owl.", flaggedCopy);
            Assert.DoesNotContain("request line", unflaggedCopy);
        }
    }

    public static class SadPathStructuralAbsence
    {
        // The known, station/catalog-owned string members on the two types the fulfilled-request
        // marker actually rides end to end (RotationCandidate -> MediaItem -> SegmentRequest.Track,
        // SPEC F87.6/F87.7, PLAN T90/T91) — every one of these is catalog metadata or station/persona
        // identity, never anything a listener typed. A future field named for wish text, a parsed
        // predicate, or any other listener-supplied fragment would show up here as an unexpected extra
        // member and fail this pin — F87.7's "absence by construction" made structural, not just
        // documented.
        static readonly string[] SegmentRequestStringMembers = ["Voice", "StationName", "StationId", "PersonaName"];
        static readonly string[] MediaItemStringMembers = ["MediaId", "Locator", "Title", "Artist", "Album", "Genre"];

        [Fact]
        public static void PromptAssemblyHasNoParameterThatCouldCarryListenerText()
        {
            AssertOnlyKnownStringMembers(typeof(SegmentRequest), SegmentRequestStringMembers);
            AssertOnlyKnownStringMembers(typeof(MediaItem), MediaItemStringMembers);
        }

        static void AssertOnlyKnownStringMembers(Type type, IReadOnlyCollection<string> whitelist)
        {
            var actualStringMembers = type.GetProperties()
                .Where(p => p.PropertyType == typeof(string))
                .Select(p => p.Name)
                .Order()
                .ToList();

            Assert.Equal(whitelist.Order(), actualStringMembers);
        }
    }
}
