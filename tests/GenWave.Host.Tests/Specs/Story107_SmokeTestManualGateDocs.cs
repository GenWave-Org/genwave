// STORY-107 — Smoke test documented as a manual gate (Epic R / SPEC F32, gitea-#179)
//
// BDD specification — xUnit. R12 un-pins this file. The isolated scratch-project procedure is
// documented alongside the script; no unreconciled CI-gate claim remains in repo docs.
// File-content facts follow the shipped grep-assert idiom (Story074/Story102's RepoRoot pattern).

using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSmokeTestManualGateDocs
{
    // ---------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Repo root, resolved relative to the test assembly's build output — the convention
    /// Story074/Story102 use for reaching repo-root files from a test project.
    /// </summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string SmokeTestScriptText =>
        File.ReadAllText(Path.Combine(RepoRoot, "tools", "smoke_test.sh"));

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioProcedureAlongsideTheScript
    {
        [Fact]
        public void ScriptHeaderDocumentsTheIsolatedScratchProjectProcedure()
        {
            // tools/smoke_test.sh's header names: an explicit -p scratch project, its own .env,
            // its own ports/volumes, and never a live station (F32.2).
            var header = SmokeTestScriptText;

            Assert.Contains("docker compose -p <scratch-project>", header, StringComparison.Ordinal);
            Assert.Contains("own `.env`", header, StringComparison.Ordinal);
            Assert.Contains("own ports", header, StringComparison.Ordinal);
            Assert.Contains("own volumes", header, StringComparison.Ordinal);
            Assert.Contains("NEVER RUN AGAINST A LIVE STATION", header, StringComparison.Ordinal);
        }

        [Fact]
        public void ScriptHeaderStatesCiDoesNotRunIt()
        {
            var header = SmokeTestScriptText;

            Assert.Contains("MANUAL PRE-RELEASE GATE", header, StringComparison.Ordinal);
            Assert.Contains("CI intentionally does NOT run this script", header, StringComparison.Ordinal);
            Assert.Contains("SPEC F32", header, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioNoStaleCiClaims
    {
        // Grep README.md + docs/*.md for smoke-test-in-CI claims; the only legal hit is
        // PROJECT.md's success criterion (flagged for /explore, out of R12's scope).

        /// <summary>
        /// A line is a CI-gate "claim" candidate if it says the smoke test passes/runs "in CI"
        /// or names it "a CI gate". Deliberately loose (not a literal-string match on the exact
        /// PROJECT.md wording) so a differently-worded future regression still trips this.
        /// </summary>
        static readonly Regex ClaimPattern =
            new(@"passes?\s+in\s+CI\b|CI\s+gate\b", RegexOptions.IgnoreCase);

        /// <summary>Reported-speech markers: "X was described as a CI gate" cites the old claim, it doesn't assert it.</summary>
        static readonly string[] CitationMarkers =
            ["described as", "was called", "claimed", "claiming", "calling it"];

        /// <summary>Catches "not a CI gate" / "no CI gate" / "CI never ... gate" immediately ahead of the match.</summary>
        static readonly Regex NegatedImmediatelyBefore =
            new(@"\b(not|no|never)\b[\w\s]{0,12}$", RegexOptions.IgnoreCase);

        /// <summary>
        /// README.md + every docs/*.md file. GenWave_PRD.md and GenWave_RoadMap.md at the repo
        /// root are deliberately excluded: docs/ARCHITECTURE.md's header names them design-history
        /// artifacts that lose to this doc set on disagreement ("when they disagree with this doc,
        /// this doc wins"), so their old CI mentions are historical record, not a live repo claim
        /// F32 needs reconciled.
        /// </summary>
        static IEnumerable<string> SweptDocs()
        {
            yield return Path.Combine(RepoRoot, "README.md");
            // The public tree ships without docs/ (migration D-M4) — sweep it only when present.
            var docsDir = Path.Combine(RepoRoot, "docs");
            if (!Directory.Exists(docsDir))
                yield break;
            foreach (var file in Directory.EnumerateFiles(docsDir, "*.md"))
                yield return file;
        }

        /// <summary>
        /// Finds claim-pattern matches that are neither quoted-as-citation (an odd number of `"`
        /// precede the match — the SPEC.md/PROJECT.md changelog pattern of citing the old wording
        /// before correcting it), negated, nor reported speech.
        /// </summary>
        static IEnumerable<(string File, int Line, string Text)> UnreconciledClaims()
        {
            foreach (var file in SweptDocs())
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (!line.Contains("smoke", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (Match match in ClaimPattern.Matches(line))
                    {
                        var prefix = line[..match.Index];
                        var quotedAsCitation = prefix.Count(c => c == '"') % 2 == 1;
                        var negated = NegatedImmediatelyBefore.IsMatch(prefix);
                        var reportedSpeech = CitationMarkers.Any(marker =>
                            line.Contains(marker, StringComparison.OrdinalIgnoreCase));

                        if (!quotedAsCitation && !negated && !reportedSpeech)
                            yield return (file, lineNumber, line.Trim());
                    }
                }
            }
        }

        // 2026-07-14: PROJECT.md's V1 criterion was reworded via /explore (gitea-#207 closed, commit
        // af1af7c) — the one previously-tolerated hit is gone, so the assertion is now zero.
        [Fact]
        public void NoUnreconciledCiGateClaimRemainsInRepoDocs()
        {
            var offenders = UnreconciledClaims().ToList();

            Assert.True(offenders.Count == 0,
                "Unreconciled CI-gate claim(s) — the smoke test is a manual pre-release gate " +
                "(SPEC F32, gitea-#207 resolved 2026-07-14); reconcile the doc:\n" +
                string.Join('\n', offenders.Select(o => $"  {o.File}:{o.Line}: {o.Text}")));
        }
    }
}
