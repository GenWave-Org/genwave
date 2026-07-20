// STORY-181 — Compose host-publish guard in CI
//
// BDD specification — xUnit (SPEC F67.1). Pending scaffold; /build-loop (PLAN T25)
// implements and removes Skip. These facts drive the guard script via Process against
// real/fixture overlays. AC2 (CI workflow wiring) is verified in T25's review, not
// unit-specced; F67.2 outside-in verification is the 🖐️ T24 manual task.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureComposeHostPublishGuard
{
    private const string Pending = "pending — PLAN T25 (/build-loop)";

    public static class ScenarioCurrentOverlayPasses
    {
        [Fact(Skip = Pending)]
        public static void Guard_exits_zero_reporting_only_caddy_published()
        {
            // Given the merged config of compose.yaml + compose.demo.yaml
            // When  the guard script runs
            // Then  it exits 0, reporting 0.0.0.0 publishes only for caddy 80/443 (F67.1)
            Assert.Fail(Pending);
        }
    }

    public static class SadPathRegression
    {
        [Fact(Skip = Pending)]
        public static void Reintroduced_public_publish_fails_naming_service_and_port()
        {
            // Given a test overlay re-adding a 0.0.0.0 publish on a non-proxy service
            // When  the guard script runs against it
            // Then  it exits non-zero naming the offending service and port (F67.1)
            Assert.Fail(Pending);
        }
    }
}
