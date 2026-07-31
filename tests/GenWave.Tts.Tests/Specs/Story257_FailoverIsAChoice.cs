// STORY-257 — Failover is a choice, not a default (SPEC F99.2–F99.4)
//
// Dean's ruling, verbatim: "seems redundant to spin up Piper and then set it to never-use —
// we might want to rethink the whole failover setup and make it opt-in instead of the current
// always-on."
//
// This is mostly a CONFIGURATION AND PACKAGING change rather than new machinery: gh-#147
// already built the empty-chain semantics (`TtsFallbackChain.Empty` makes
// FallbackTtsSynthesizer a transparent pass-through to the primary — no health read, no
// retry, no second exception). What changes is the shipped default and whether the sidecar
// container runs at all.
//
// ⚠️ Ruled to apply to existing installs too, on the stated grounds that the demo box is
// currently the only installation. That rationale HAS AN EXPIRY DATE — it stops being true
// the first time a stranger runs a station, and must not be cited as precedent afterwards.
//
// F99.4 is the subtle one: on the piper-only topology Piper is the PRIMARY engine, not a
// fallback, so voice integrity is satisfied by Piper producing the DJ's own configured voice.
// That topology must configure it as primary rather than leaning on a chain.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureFailoverIsAChoice
{
    public static class ScenarioTheShippedDefaultConfiguresNoChain
    {
        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void A_fresh_install_resolves_an_empty_chain()
        {
            // var chain = TtsFallbackChain.Resolve(new TtsFallbackOptions());
            // Assert.True(chain.IsEmpty);
            Assert.Fail("pending T148");
        }

        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void The_shipped_compose_no_longer_sets_a_fallback_endpoint()
        {
            // The legacy flat key is what makes the chain non-empty today.
            Assert.Fail("pending T148");
        }

        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void An_empty_chain_is_a_transparent_pass_through()
        {
            // gh-#147's existing contract, now the default: a primary failure propagates
            // exactly as it did before any fallback feature existed.
            Assert.Fail("pending T148");
        }
    }

    public static class ScenarioNoChainMeansNoSidecar
    {
        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void The_default_render_starts_no_fallback_engine_container()
        {
            // Rendered from the shipped compose files, the way the gh-#310 specs assert
            // service presence.
            Assert.Fail("pending T148");
        }

        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void The_broadcast_path_is_otherwise_unchanged()
        {
            // db / icecast / engine / api / kokoro all still present — this removes a sidecar,
            // not a topology.
            Assert.Fail("pending T148");
        }
    }

    public static class ScenarioOptingInRestoresSubstitution
    {
        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void A_configured_profile_chain_renders_on_primary_failure()
        {
            Assert.Fail("pending T148");
        }

        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void The_configured_hop_order_is_honoured()
        {
            Assert.Fail("pending T148");
        }
    }

    public static class ScenarioPiperOnlyIsUnaffectedInKind
    {
        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void Piper_is_configured_as_the_primary_engine()
        {
            // F99.4 — not as a fallback hop that voice integrity would then refuse to use.
            Assert.Fail("pending T148");
        }

        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void A_break_airs_in_the_dj_configured_voice_on_that_topology()
        {
            Assert.Fail("pending T148");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioAnOptedInChainThatFails
    {
        [Fact(Skip = "Pending T148 — see docs/PLAN.md")]
        public static void Total_chain_failure_drops_the_break_under_voice_integrity()
        {
            // Opting into substitution does not opt out of F99.1 — when every hop fails the
            // break is dropped, never aired in some other voice.
            Assert.Fail("pending T148");
        }
    }
}
