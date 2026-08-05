// STORY-288 — Uninstall with a guard (SPEC F104.14 · PLAN T208)
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeaturePackUninstall
{
    public sealed class ScenarioUnreferencedPacksUninstall
    {
        [Fact(Skip = "pending T208 (STORY-288 AC1)")]
        public void AnUnreferencedPackRemovesTransactionally() { }

        [Fact(Skip = "pending T208 (STORY-288 AC1)")]
        public void ItsFacesStopServingOnTheNextRequest() { }
    }

    public sealed class ScenarioReferencedPacksRefuse
    {
        [Fact(Skip = "pending T208 (STORY-288 AC2)")]
        public void TheRefusalNamesEveryReferencingSavedTheme() { }

        [Fact(Skip = "pending T208 (STORY-288 AC2)")]
        public void NothingIsRemovedOnARefusal() { }
    }
}
