// STORY-221 — The stream survives Icecast 2.5 (SPEC F88.1, PLAN T82)
//
// BDD specification — xUnit, authored PENDING at /plan time (house rule since Epic S).
// The image itself is Docker infra; these facts pin the OBSERVABLE contract: the Dockerfile
// builds a pinned 2.5.0 source tarball, and the T26-style metadata capture/smoke gates are
// re-run against it (their own suites; referenced here as the definition of done).

namespace GenWave.Host.Tests.Specs;

public static class FeatureIcecastTwoFive
{
    public static class ScenarioPinnedSourceBuild
    {
        [Fact(Skip = "Pending — PLAN T82 (/build-loop)")]
        public static void DockerfileBuildsThePinnedTwoFiveTarball()
        {
            // Given icecast/Dockerfile  When read  Then it compiles icecast-2.5.0 from a
            // pinned URL — one assertion on the version string.
        }

        [Fact(Skip = "Pending — PLAN T82 (/build-loop)")]
        public static void DockerfilePinsTheTarballSha256()
        {
            // Then a sha256 verification guards the download — one assertion on its presence.
        }
    }

    public static class ScenarioMetadataContractHolds
    {
        [Fact(Skip = "Pending — PLAN T82 (/build-loop)")]
        public static void StreamTitleBuilderInputsRemainPinned()
        {
            // Given the Story182 static guard  Then its pins hold unchanged on the new image
            // (the live T26 capture re-run is the 🖐️ manual half of T82's acceptance).
        }
    }
}
