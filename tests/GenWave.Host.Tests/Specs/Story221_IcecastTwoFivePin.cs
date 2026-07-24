// STORY-221 — The stream survives Icecast 2.5 (SPEC F88.1, PLAN T82)
//
// BDD specification — xUnit, authored PENDING at /plan time (house rule since Epic S), turned
// live by T82. The image itself is Docker infra; these facts pin the OBSERVABLE contract: the
// Dockerfile builds a pinned 2.5.0 source tarball with a SHA256 verification gate, and the
// StreamTitle builder Story182 guards is untouched by the rebuild. The T26-style metadata
// capture/smoke gates are re-run against the built image as T82's own manual half (not exercised
// here — see PLAN.md T82 and this file's own build/run evidence in the task report).

using System.Security.Cryptography;

namespace GenWave.Host.Tests.Specs;

public static class FeatureIcecastTwoFive
{
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static string DockerfileText => File.ReadAllText(Path.Combine(RepoRoot(), "icecast", "Dockerfile"));

    public static class ScenarioPinnedSourceBuild
    {
        [Fact]
        public static void DockerfileBuildsThePinnedTwoFiveTarball()
        {
            // Given icecast/Dockerfile  When read  Then it compiles icecast-2.5.0 from the exact
            // pinned upstream tarball URL — one assertion on that literal URL's presence.
            Assert.Contains(
                "https://downloads.xiph.org/releases/icecast/icecast-2.5.0.tar.gz",
                DockerfileText,
                StringComparison.Ordinal);
        }

        [Fact]
        public static void DockerfilePinsTheTarballSha256()
        {
            // Then a sha256sum -c verification gate guards that exact download — one assertion
            // matching the pinned 64-hex-char digest declared AND piped into the check (not just
            // declared and ignored), so a mismatched download fails the build.
            Assert.Matches(
                @"ARG ICECAST_TARBALL_SHA256=[0-9a-f]{64}\b[\s\S]*?sha256sum -c -",
                DockerfileText);
        }
    }

    public static class ScenarioMetadataContractHolds
    {
        // Pinned 2026-07-23 (PLAN T82); re-pinned 2026-07-23 (PLAN T85, STORY-223, SPEC F88.4) —
        // the epoch change T85's own PLAN line sanctions: output.icecast now carries an explicit
        // icy_metadata=[...] list (the engine default plus "url"), so the C# feeder's
        // Station:PublicBaseUrl-driven url= annotation actually reaches the ICY StreamUrl a
        // metadata-aware client sees. engine/genwave.liq's gw_icy_song StreamTitle builder —
        // statically guarded by Story182/FeatureListenerMetadataDisclosure to consume only
        // artist/title and touch no internal metadata key — is untouched by either this rebuild
        // or T85's icy_metadata addition. Re-asserting Story182's own "artist/title only" fact
        // here would duplicate it verbatim, so this pins the file's bytes instead: unlike a
        // targeted content check, a whole-file hash also catches an edit Story182's regexes
        // wouldn't (e.g. reordering outside the gw_icy_song block). The live T26 ICY/status-page
        // capture against the running 2.5.0 image is T82's manual half; the live per-track
        // StreamUrl observation against the compose stack is T93/T94's — neither is exercised here.
        //
        // T93 epoch (F88.4 export fix) — T85's icy_metadata addition alone was not sufficient:
        // settings.encoder.metadata.export (the gate a few lines above icy_metadata in the file)
        // never carried "url", so the annotation was filtered out before icy_song/icy_metadata saw
        // it and StreamUrl never reached listeners (Story230 gate's live-run finding). Re-pinned
        // to the one-line export-list fix.
        const string EngineScriptSha256 = "11c8b3b59b4b641dc59fa4217e935442573adf04f8e756934e23593b17677049";

        [Fact]
        public static void StreamTitleBuilderInputsRemainPinned()
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepoRoot(), "engine", "genwave.liq"));
            Assert.Equal(EngineScriptSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }
}
