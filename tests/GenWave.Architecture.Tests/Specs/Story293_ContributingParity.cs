// STORY-293 — The laws are front-and-center for contributors (SPEC F105.3, F105.5 · PLAN T215)
using GenWave.Architecture.Tests.Support;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// Feature: the shipped-doc half. CONTRIBUTING.md carries the six-law table + the seam
/// criterion before any workflow detail, and a parity test ties doc to suite so neither
/// drifts. Built at T215, which waited for all law ids to settle (T212–T214).
///
/// <b>Table shape — 7 rows, one per <see cref="LawId"/> constant (T215's call, PLAN's "your call"
/// note).</b> L4's two halves (<see cref="LawId.L4References"/>, <see cref="LawId.L4Immutability"/>)
/// get their own rows rather than one merged "L4" row. ARCHITECTURE.md's own table stays SIX laws
/// (its rationale column groups L4 back into one row — that section owns the narrative, not the
/// parity contract); CONTRIBUTING's table instead mirrors the suite's seven ids exactly, one row per
/// id, because that is what makes the reconciliation — and STORY-293 AC3's "drop one row -> exactly
/// that id goes red, never two" mutation-check — hold with no special-casing: one row always means
/// one id, in both directions.
///
/// <b>Extraction convention (see <see cref="ContributingLawTable"/>'s own remarks).</b> A law id
/// counts only when it's the backtick-quoted FIRST cell of a markdown table row
/// (<c>| `L1` | ... |</c>) — prose mentions of the same text elsewhere in the file (there are several,
/// including in this very doc comment) never count, and CONTRIBUTING.md needs no special "don't count
/// me" marker anywhere to keep them from counting.
/// </summary>
public sealed class FeatureContributingParity
{
    public sealed class ScenarioFrontAndCenter
    {
        private readonly string contributing = ContributingDocument.Read();

        [Fact]
        public void TheLawsTableAppearsBeforeAnyWorkflowDetail()
        {
            var governanceIndex = ContributingDocument.IndexOfHeadingContaining(contributing, "Architecture governance");
            var workflowIndex = ContributingDocument.IndexOfHeadingContaining(contributing, "Development");

            Assert.True(governanceIndex >= 0, "CONTRIBUTING.md has no \"Architecture governance\" heading.");
            Assert.True(workflowIndex >= 0, "CONTRIBUTING.md has no \"Development\" (workflow) heading.");
            Assert.True(
                governanceIndex < workflowIndex,
                "The laws table must appear before the first workflow-detail heading (STORY-293 AC1).");
        }

        [Fact]
        public void TheSeamCriterionAppearsWithTheTable()
        {
            var governanceIndex = ContributingDocument.IndexOfHeadingContaining(contributing, "Architecture governance");
            var workflowIndex = ContributingDocument.IndexOfHeadingContaining(contributing, "Development");
            var seamCriterionIndex = contributing.IndexOf(
                "does a third-party module need to implement or consume this?",
                StringComparison.OrdinalIgnoreCase);

            Assert.True(seamCriterionIndex >= 0, "CONTRIBUTING.md is missing the seam-placement criterion's wording.");
            Assert.True(
                seamCriterionIndex > governanceIndex && seamCriterionIndex < workflowIndex,
                "The seam-placement criterion must live in the same front-and-center section as the laws table, " +
                "before any workflow detail.");
        }

        [Fact]
        public void TheGovernanceSectionLinksBothIssues()
        {
            Assert.Contains("gh-#398", contributing, StringComparison.Ordinal);
            Assert.Contains("gh-#400", contributing, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioSuiteDocParity
    {
        [Fact]
        public void EveryLawIdInTheSuiteAppearsInContributingAndViceVersa()
        {
            var docIds = ContributingLawTable.ExtractLawIds(ContributingDocument.Read());
            var result = LawParity.Compare(LawId.All, docIds);

            Assert.True(result.IsClean, LawParity.Format(result));
        }
    }

    public sealed class ScenarioDriftIsRed
    {
        // Hermetic probes: synthetic markdown against a fixed stand-in id list, never the live
        // CONTRIBUTING.md or the live LawId.All — proves the extraction+diff pipeline discriminates a
        // missing/extra id correctly, decoupled from today's actual law count (the same "fixture,
        // never production" precedent every other law's ScenarioViolationsAreRedAndNamed already
        // sets: Story290/291/292).
        private static readonly IReadOnlyList<string> ProbeSuiteIds = new[] { "L1", "L2", "L3" };

        private const string TableMissingARow = """
            | Law | Rule | Why |
            |---|---|---|
            | `L1` | one | one-why |
            | `L3` | three | three-why |
            """;

        private const string TableWithAPhantomRow = """
            | Law | Rule | Why |
            |---|---|---|
            | `L1` | one | one-why |
            | `L2` | two | two-why |
            | `L3` | three | three-why |
            | `L9` | nine | nine-why |
            """;

        [Fact]
        public void ALawRowRemovedFromTheTableFailsParityNamingTheMissingId()
        {
            var docIds = ContributingLawTable.ExtractLawIds(TableMissingARow);
            var result = LawParity.Compare(ProbeSuiteIds, docIds);

            Assert.False(result.IsClean);
            Assert.Equal(new[] { "L2" }, result.MissingFromDoc);
            Assert.Empty(result.ExtraInDoc);
            Assert.Contains("L2", LawParity.Format(result), StringComparison.Ordinal);
        }

        // AC2's "vice versa" half: a table row naming an id the suite has never heard of is just as
        // much drift as a missing one, and must be named too — the extra-in-doc direction
        // EveryLawIdInTheSuiteAppearsInContributingAndViceVersa also exercises live, proven here in
        // isolation with a synthetic phantom id ("L9") no real law will ever collide with.
        [Fact]
        public void APhantomRowInTheTableFailsParityNamingTheExtraId()
        {
            var docIds = ContributingLawTable.ExtractLawIds(TableWithAPhantomRow);
            var result = LawParity.Compare(ProbeSuiteIds, docIds);

            Assert.False(result.IsClean);
            Assert.Empty(result.MissingFromDoc);
            Assert.Equal(new[] { "L9" }, result.ExtraInDoc);
            Assert.Contains("L9", LawParity.Format(result), StringComparison.Ordinal);
        }

        // The extraction convention's own proof (ContributingLawTable's remarks name this exact
        // hazard): a bare prose mention of a law id — no leading pipe, no backtick-quoted first
        // cell — must never be mistaken for a table row.
        [Fact]
        public void AProseMentionOfALawIdIsNeverExtractedAsATableRow()
        {
            const string proseOnly = "L2 gets discussed at length elsewhere, but never once as a table row here.";

            Assert.Empty(ContributingLawTable.ExtractLawIds(proseOnly));
        }
    }
}
