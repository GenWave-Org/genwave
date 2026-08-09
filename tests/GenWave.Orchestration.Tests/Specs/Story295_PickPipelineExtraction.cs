// STORY-295 — The pick pipeline leaves the Orchestrator (F112, gh-#401)

using System.Reflection;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeaturePickPipelineExtraction
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePolicyOwnsTheLadder
    {
        [Fact]
        public void MusicSelectionPolicyTypeExistsInOrchestration()
        {
            var policyType = typeof(Orchestrator).Assembly.GetType("GenWave.Orchestration.MusicSelectionPolicy");

            Assert.NotNull(policyType);
        }

        [Fact]
        public void PolicyConsumesOnlyTheSelectionSeams()
        {
            // Constructor parameters ⊆ { IMediaCatalog, IEnvelopeProvider, IPersonaPickProvider,
            // IRequestFulfillmentSource, ILogger<MusicSelectionPolicy> } — BoundaryFitPlan is a
            // method argument, never constructor state (checked by its absence from this allow-list).
            var policyType = typeof(Orchestrator).Assembly.GetType("GenWave.Orchestration.MusicSelectionPolicy");
            Assert.NotNull(policyType);

            var allowedParameterTypeNames = new HashSet<string>
            {
                "IMediaCatalog",
                "IEnvelopeProvider",
                "IPersonaPickProvider",
                "IRequestFulfillmentSource",
                "ILogger`1",
            };

            var constructor = Assert.Single(policyType!.GetConstructors());
            Assert.All(
                constructor.GetParameters(),
                parameter => Assert.Contains(parameter.ParameterType.Name, allowedParameterTypeNames));
        }

        [Fact]
        public void BoundaryFitPlanIsPassedIntoThePolicy()
        {
            // The selection entry point's signature carries BoundaryFitPlan (the Orchestrator-side
            // BuildBoundaryFit result) — reflection over the policy's own methods, public or not
            // (the entry point stays internal since BoundaryFitPlan itself is internal planning
            // state that never crosses the assembly boundary).
            var policyType = typeof(Orchestrator).Assembly.GetType("GenWave.Orchestration.MusicSelectionPolicy");
            Assert.NotNull(policyType);

            var methods = policyType!.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.Contains(
                methods,
                method => method.GetParameters().Any(p => p.ParameterType.Name == "BoundaryFitPlan"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheOrchestratorNoLongerPicks
    {
        [Fact]
        public void NoSelectionRungMembersRemainOnOrchestrator()
        {
            // Reflection: Orchestrator has no SelectMusicCandidateAsync / SelectEnvelopeAwareCandidateAsync /
            // SelectEnvelopeLadderAsync / TryFulfillPendingRequestAsync / TryPersonaPickAsync members.
            var forbiddenMemberNames = new HashSet<string>
            {
                "SelectMusicCandidateAsync",
                "SelectEnvelopeAwareCandidateAsync",
                "SelectEnvelopeLadderAsync",
                "TryFulfillPendingRequestAsync",
                "TryPersonaPickAsync",
            };

            var offendingMembers = typeof(Orchestrator)
                .GetMembers(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                .Where(member => forbiddenMemberNames.Contains(member.Name));

            Assert.Empty(offendingMembers);
        }
    }
}
