// STORY-456 — The speaker travels with the plan — the seam index (gh-#772 · SPEC F189.7 · PLAN T528)
//
// BDD specification — xUnit. AC11 reads the committed SEAMS.md and asserts it lists the seams this
// story adds. AC11's byte-identity half is a global invariant over one generated file, deliberately
// NOT re-asserted here: it is owned by Story294_SeamIndex.cs's
// TheCommittedSeamsFileMatchesAFreshGenerationByteForByte (no Category trait, so it runs in the PR
// tier) and by tools/check-seam-index.sh at ci.yml:39. Those two are what forbid hand-editing
// SEAMS.md, and that is what makes the Contains facts below load-bearing — a third copy here would
// buy no coverage and cost three WebApplicationFactory<Program> builds (the T528 round-1 finding).

using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureSeamIndexListsTheSpeakerSource
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheCommittedSeamIndex
    {
        readonly string committed;

        // Given: SEAMS.md at the repo root, as committed.
        public ScenarioTheCommittedSeamIndex()
        {
            var path = Path.Combine(SolutionLocator.Root(), "SEAMS.md");
            committed = File.ReadAllText(path);
        }

        /// <summary>AC11 — the committed index lists the speaker snapshot seam T525 registered
        /// (GenWave.Tts's <c>TryAddSingleton&lt;ISpeakerSnapshotSource, SpeakerSnapshotSource&gt;()</c>).</summary>
        [Fact]
        public void ListsISpeakerSnapshotSource() =>
            Assert.Contains("ISpeakerSnapshotSource", committed, StringComparison.Ordinal);

        /// <summary>AC11 — the committed index lists the card-by-id seam T525 registered
        /// (GenWave.Host's <c>AddSingleton&lt;IPersonaCardByIdSource, PersonaCardByIdStore&gt;()</c>).</summary>
        [Fact]
        public void ListsIPersonaCardByIdSource() =>
            Assert.Contains("IPersonaCardByIdSource", committed, StringComparison.Ordinal);
    }
}
