// STORY-212 — The envelope is law, and silence is forbidden (epic acceptance gate)
//
// Zero-diff gate for the Personalities on Air epic (SPEC F79–F85), Epic V/X convention
// (see Story141/147/153/162): this epic promises zero engine/genwave.liq and compose.yaml
// diffs — selection, taste, portability, and mood work live entirely in the .NET host,
// the catalog, and the admin UI. These facts run non-Skip from day one; an intentional
// edit from a LATER epic re-pins with a dated comment, per the standing convention.
//
// ComposeYamlSha256 pinned 2026-07-21 at epic start: the post-PR-#68 compose.yaml
// (kokoro healthcheck curls /health — see the F78-era re-pin trail in the four Q3 gates).
// EngineScriptSha256 unchanged since the same trail.

using System.Security.Cryptography;

namespace GenWave.Host.Tests.Specs;

public static class FeatureAcceptanceGatePersonalitiesOnAir
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107/141's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string Sha256Hex(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, relativePath));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static class ScenarioEngineAndComposeCarryZeroDiffFromMain
    {
        const string EngineScriptSha256 = "a256fd3f2797ed9b52e3f8507e8ca610aa02218e2fedc5c231369f0ccaab9bd6";
        const string ComposeYamlSha256  = "9ddd169329ef5b092638d1e67279272fc4d7b9f350dcc330cb455d7d92faf981";

        [Fact]
        public static void EngineScriptByteMatchesMain()
        {
            Assert.Equal(EngineScriptSha256, Sha256Hex(Path.Combine("engine", "genwave.liq")));
        }

        [Fact]
        public static void ComposeYamlByteMatchesMain()
        {
            Assert.Equal(ComposeYamlSha256, Sha256Hex("compose.yaml"));
        }
    }
}
