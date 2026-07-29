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
        // T93 epoch (F88.4 export fix) — settings.encoder.metadata.export now carries "url" too,
        // so the C# feeder's url= annotation actually reaches the ICY StreamUrl (see the
        // Story230 gate's live-run finding).
        const string EngineScriptSha256 = "869ea6fc35e3d73de4ca6cc47551a07da63bd855481ba77120fe73f1754d72da";
        const string ComposeYamlSha256  = "51e394e654f1ab1049503eb1c99d7d43c286b69069b9b88f4b78ce37a7f3438c";

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
