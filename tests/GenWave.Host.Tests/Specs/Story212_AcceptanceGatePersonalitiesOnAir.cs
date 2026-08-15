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
        //
        // ComposeYamlSha256 re-pinned 2026-07-29 (gh-#241): the piper service moves off the
        // amd64-only artibex/piper-http digest onto the repo-built ./piper build context
        // (published multi-arch per release via the gh-#240 matrix). Same wire shape, env,
        // volume, and healthcheck contract — a piper-service-only edit, another intentional
        // edit from a LATER epic, not a regression of this epic's zero-diff promise.
        // EngineScriptSha256 unchanged — gh-#241 does not touch engine/genwave.liq.
        const string EngineScriptSha256 = "869ea6fc35e3d73de4ca6cc47551a07da63bd855481ba77120fe73f1754d72da";
        // ComposeYamlSha256 re-pinned 2026-07-30 (gh-#276): kokoro mem_limit 3g->4g + comment
        // refresh — ops-only edit, no service/wire/volume change. Another intentional edit from
        // a later epic, not a regression of this epic's zero-diff promise.
        // ComposeYamlSha256 re-pinned 2026-08-02 (gh-#334): Library__EnrichmentConcurrency becomes
        // ${LIBRARY_ENRICHMENT_CONCURRENCY:-4} — same default on every existing box, now overridable.
        // A pinned appliance box sets Admin__Enabled=false, closing PUT /api/settings, so the hardcoded
        // value left the operator no lever at all while enrichment pinned all four cores of a Pi 5.
        // Config-indirection only — no service, wire, volume or healthcheck change. Another intentional
        // edit from a later epic, not a regression of this epic's zero-diff promise.
        // ComposeYamlSha256 re-pinned 2026-08-14 (PLAN T148, SPEC F99.2/F99.3, STORY-257): TTS
        // failover becomes opt-in — Tts__Fallback__Endpoint/Tts__Fallback__Voice no longer ship on
        // the api service (the shipped default now resolves an empty fallback chain), and the
        // piper service gains `profiles: ["fallback"]` (off by default; an operator opts in with
        // `--with fallback` plus a live PUT /api/settings). Another intentional edit from a later
        // epic, not a regression of this epic's zero-diff promise.
        const string ComposeYamlSha256  = "f6d3d83923562ee2d6fde56749b5099c8ecea079f6d019101fd9c3cb73dbb27c";

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
