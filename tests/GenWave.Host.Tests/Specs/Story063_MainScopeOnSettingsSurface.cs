// STORY-063 — Main rotation scope joins the live settings surface
//
// BDD specification — xUnit. Station:Scope:LibraryIds registers in the F19 AllowedSetting
// registry (live, number-list) with the existing non-empty StationOptions validation intact:
// PUT [] must 400 and persist nothing (an empty MAIN scope is a silent station — unlike
// SafeScope, whose empty is legal per F21.5). The UI half (K5-style picker, inline
// empty-rejection) is specced in admin-ui/__specs__/main-scope-picker.spec.tsx.
//
// Registry facts are runnable and red until M3 lands. The live-apply proof (a PUT widens
// what GET /media/random can return, no api restart) is the M3 wire acceptance —
// operator-gated Integration.

using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureMainScopeOnSettingsSurface
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — allowlist registry entry (runnable, red until M3)
    // ---------------------------------------------------------------------

    public sealed class ScenarioMainScopeIsAnAllowedSetting
    {
        readonly AllowedSetting? entry =
            StationSettingsAllowlist.All.SingleOrDefault(s => s.Key == "Station:Scope:LibraryIds");

        [Fact]
        public void TheKeyIsRegistered()
        {
            Assert.NotNull(entry);
        }

        [Fact]
        public void ItAppliesLive()
        {
            Assert.Equal(SettingApplyMode.Live, entry?.ApplyMode);
        }

        [Fact]
        public void ItIsANumberList()
        {
            Assert.Equal(SettingKind.NumberList, entry?.Kind);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — empty main scope stays rejected (validator discipline)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioEmptyMainScopeIsRejected
    {
        const string Skip = "Settings pipeline: exercised in M3 via the Story058-pattern factory (PUT [] -> 400, no persist, feeder keeps previous scope).";

        [Fact(Skip = Skip)]
        public void PutOfAnEmptyListReturns400ProblemDetails() { }

        [Fact(Skip = Skip)]
        public void NothingIsPersistedOnTheRejectedPut() { }
    }

    // ---------------------------------------------------------------------
    // WIRE — live apply (operator-gated)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioLivePutWidensSelection
    {
        const string Skip = "Live stack + operator: M3 wire acceptance (PUT [1,2] makes a library-2 row selectable, no api restart).";

        [Fact(Skip = Skip)]
        public void ARowParkedInLibraryTwoBecomesSelectableAfterALivePut() { }
    }
}
