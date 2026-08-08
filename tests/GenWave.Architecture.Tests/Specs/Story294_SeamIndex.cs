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
/// convention — its red-on-stale half is T217's CI wire, exercised at that task's
/// acceptance rather than as a unit fact). T216 built at this task; T217 wires CI + the
/// CONTRIBUTING check-first line, still pending below.
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

    public sealed class ScenarioTheCheckFirstLine
    {
        [Fact(Skip = "pending — T217 builds this (STORY-294 AC3)")]
        public void ContributingInstructsCheckingSeamsBeforeAddingASeam() => Assert.Fail("pending");
    }
}
