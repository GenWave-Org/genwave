// STORY-294 — SEAMS.md: the generated map with a drift gate (SPEC F105.6 · PLAN T216, T217)
using GenWave.Architecture.Tests.Support;
using GenWave.SeamIndexGenerator;

namespace GenWave.Architecture.Tests.Specs;

/// <summary>
/// One <c>WebApplicationFactory&lt;Program&gt;</c> build (<see cref="SeamIndexDocument.Generate"/>,
/// via <c>SeamCompositionSnapshot</c>) shared across <see cref="FeatureSeamIndex"/>'s scenarios that
/// only need to READ a generation — <see cref="FeatureSeamIndex.ScenarioDeterministicGeneration"/>'s
/// own "two runs" fact still calls <see cref="SeamIndexDocument.Generate"/> a second time itself
/// (that IS the fact), so the class carries this build count from four down to two, not one.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SeamIndexCollection : ICollectionFixture<SeamIndexCollection.GeneratedSeamIndex>
{
    public const string Name = "SeamIndex";

    public sealed class GeneratedSeamIndex
    {
        public string Markdown { get; } = SeamIndexDocument.Generate();
    }
}

/// <summary>
/// Feature: the generated seam index. A deterministic generator over the composition
/// root's DI registrations produces the committed root SEAMS.md (port → default adapter →
/// binding site → decorators); CI rebuilds and byte-diffs it (the catalog index.json
/// convention — its red-on-stale half is T217's CI wire, <c>tools/check-seam-index.sh</c>,
/// exercised at that task's acceptance rather than as a unit fact). T216 built the generator;
/// T217 wires CI and the CONTRIBUTING check-first line this feature's own
/// <see cref="ScenarioTheCheckFirstLine"/> now pins.
///
/// <see cref="SeamIndexDocument.Generate"/> is the SAME code both this suite and
/// <c>tools/SeamIndexGenerator</c>'s <c>Main</c> call — see its own remarks and
/// <c>SeamCompositionSnapshot</c>'s (in <c>GenWave.Host.Tests</c>) for the mechanism: a real
/// <c>WebApplicationFactory&lt;Program&gt;</c> built under the same minimal, DB-free config
/// <c>GenWave.Host.Tests</c>'s own specs already prove is enough, every GenWave.* interface port
/// resolved to its actual concrete adapter — never a hand-typed re-statement of Program.cs's
/// registrations that could silently drift.
/// </summary>
public sealed class FeatureSeamIndex
{
    [Collection(SeamIndexCollection.Name)]
    public sealed class ScenarioDeterministicGeneration(SeamIndexCollection.GeneratedSeamIndex generated)
    {
        [Fact]
        public void TwoRunsOverTheSameTreeProduceByteIdenticalOutput()
        {
            var second = SeamIndexDocument.Generate();

            Assert.Equal(generated.Markdown, second, StringComparer.Ordinal);
        }

        [Fact]
        public void EveryGenWaveSeamListsPortDefaultAdapterAndBindingSite()
        {
            string? bindingSite = null;
            var seamCount = 0;

            foreach (var line in generated.Markdown.Split('\n'))
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    bindingSite = line[3..];
                    continue;
                }

                if (!line.StartsWith("| `", StringComparison.Ordinal))
                    continue;

                Assert.False(
                    string.IsNullOrWhiteSpace(bindingSite),
                    $"seam row appears before any binding-site (project) heading: \"{line}\"");

                var cells = line.Split('|', StringSplitOptions.TrimEntries)
                    .Where(c => c.Length > 0)
                    .ToArray();

                Assert.True(cells.Length >= 3, $"malformed seam row (expected port/adapter/lifetime/notes): \"{line}\"");
                Assert.False(string.IsNullOrWhiteSpace(cells[0]), $"seam row missing a port: \"{line}\"");
                Assert.False(string.IsNullOrWhiteSpace(cells[1]), $"seam row missing a default adapter: \"{line}\"");
                seamCount++;
            }

            Assert.True(seamCount > 0, "SEAMS.md generation produced zero seam rows.");
        }
    }

    [Collection(SeamIndexCollection.Name)]
    public sealed class ScenarioCommittedAndCurrent(SeamIndexCollection.GeneratedSeamIndex generated)
    {
        [Fact]
        public void TheCommittedSeamsFileMatchesAFreshGenerationByteForByte()
        {
            var committedPath = Path.Combine(SolutionLocator.Root(), "SEAMS.md");
            Assert.True(
                File.Exists(committedPath),
                "SEAMS.md is missing from the repo root — run `dotnet run --project tools/SeamIndexGenerator`.");

            var committed = File.ReadAllText(committedPath);

            Assert.Equal(generated.Markdown, committed, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// AC3 — CONTRIBUTING carries the check-first instruction (T217).
    ///
    /// <b>What this fact pins (decided here — nothing upstream dictates it, same posture as
    /// <see cref="ContributingLawTable"/>'s own extraction-convention remarks).</b> Three
    /// substrings, each anchored to a specific, load-bearing token rather than a full sentence a
    /// harmless rewrite could break: the file name (<c>SEAMS.md</c>), the instruction's own verb
    /// phrase (<c>before adding a new seam</c> — distinctive enough that it can only appear as
    /// this instruction, unlike a generic word like "check"), and the exact regen command
    /// backtick-quoted (<c>dotnet run --project tools/SeamIndexGenerator</c> — the same command
    /// SEAMS.md's own generated header and this suite's failure messages already name, so a typo
    /// here would be caught the same way a typo there would). Deliberately NOT a single
    /// full-sentence match: STORY-293's parity tests set the precedent that the doc's wording may
    /// be edited (better prose, rewrapped lines) without a green fact going red over word order.
    ///
    /// <b>Scoped to the governance section (STORY-293's own boundary,
    /// <see cref="ContributingDocument.IndexOfHeadingContaining"/> from "Architecture governance"
    /// to "Development").</b> Matching anywhere in the whole file would pass even if the line
    /// drifted out of the governance section entirely, or if the three tokens scattered across
    /// unrelated parts of the doc that happen to each mention one of them — bounding the search to
    /// the same section T215's own front-and-center fact already proves is real closes both.
    /// </summary>
    public sealed class ScenarioTheCheckFirstLine
    {
        [Fact]
        public void ContributingInstructsCheckingSeamsBeforeAddingASeam()
        {
            var contributing = ContributingDocument.Read();
            var governanceIndex = ContributingDocument.IndexOfHeadingContaining(contributing, "Architecture governance");
            var workflowIndex = ContributingDocument.IndexOfHeadingContaining(contributing, "Development");

            Assert.True(governanceIndex >= 0, "CONTRIBUTING.md has no \"Architecture governance\" heading.");
            Assert.True(workflowIndex > governanceIndex, "CONTRIBUTING.md has no \"Development\" heading after it.");

            var governanceSection = contributing[governanceIndex..workflowIndex];

            Assert.Contains("SEAMS.md", governanceSection, StringComparison.Ordinal);
            Assert.Contains("before adding a new seam", governanceSection, StringComparison.Ordinal);
            Assert.Contains(
                "dotnet run --project tools/SeamIndexGenerator",
                governanceSection,
                StringComparison.Ordinal);
        }
    }
}
