// STORY-068 — Legalize empty SafeScope at both validators (WIRE)
//
// BDD specification — xUnit. SPEC F25.1/F25.2/F25.6: the boot-time
// StationOptionsValidator and the PUT-time SettingValidator both accept an empty
// Station:SafeScope:LibraryIds, while non-positive ids still fail and main scope
// (Station:Scope:LibraryIds) stays reject-empty. Runtime endpoint (F21.5) is
// unchanged. The K5 confirm-dialog `[]` submission going through with a 200
// (was 400) is the WIRE proof — operator-gated Integration below.
//
// Un-pinned facts run today against the shipped SettingValidator seam and are
// red until N1 splits IsNonEmptyPositiveLongArray. Live PUT/boot/WARN-log
// assertions are Skip-pinned Integration per the E10/W7/L8/K6/M8 pattern.

using Microsoft.Extensions.Configuration;
using GenWave.Host.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureLegalizeEmptySafeScope
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — SettingValidator accepts empty SafeScope on PUT (F25.2)
    // ---------------------------------------------------------------------

    public sealed class ScenarioSettingValidatorAcceptsEmptySafeScope
    {
        readonly SettingValidator validator = new(new ConfigurationBuilder().Build());

        [Fact]
        public void AnEmptyJsonArrayReturnsNullFromValidate()
        {
            // Validate returns null on success; a non-null string on rejection.
            Assert.Null(validator.Validate("Station:SafeScope:LibraryIds", "[]"));
        }

        [Fact]
        public void ASingleElementArrayStillReturnsNull()
        {
            // Regression: relaxing empty must not break the non-empty happy path.
            Assert.Null(validator.Validate("Station:SafeScope:LibraryIds", "[1]"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — per-element positivity survives the empty-allowed relaxation
    // ---------------------------------------------------------------------

    public sealed class ScenarioNonPositiveSafeScopeIdsStillFail
    {
        readonly SettingValidator validator = new(new ConfigurationBuilder().Build());

        [Fact]
        public void ZeroAsAnIdReturnsARejectionMessage()
        {
            Assert.NotNull(validator.Validate("Station:SafeScope:LibraryIds", "[0]"));
        }

        [Fact]
        public void ANegativeIdReturnsARejectionMessage()
        {
            Assert.NotNull(validator.Validate("Station:SafeScope:LibraryIds", "[-1]"));
        }

        [Fact]
        public void ANonNumericElementReturnsARejectionMessage()
        {
            Assert.NotNull(validator.Validate("Station:SafeScope:LibraryIds", "[\"nope\"]"));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — main scope stays reject-empty (F25.6 with F23.1)
    // ---------------------------------------------------------------------

    public sealed class ScenarioMainScopeStaysRejectEmpty
    {
        readonly SettingValidator validator = new(new ConfigurationBuilder().Build());

        [Fact]
        public void AnEmptyJsonArrayForMainScopeReturnsARejectionMessage()
        {
            // Empty main scope = silent station, not degraded mode — must stay a 400.
            Assert.NotNull(validator.Validate("Station:Scope:LibraryIds", "[]"));
        }

        [Fact]
        public void ANonEmptyMainScopeStillReturnsNull()
        {
            Assert.Null(validator.Validate("Station:Scope:LibraryIds", "[1]"));
        }
    }

    // ---------------------------------------------------------------------
    // WIRE — boot behavior: StationOptionsValidator accepts empty + WARN
    // (Integration — needs the full host + a captured log sink)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioBootWithEmptySafeScopeSucceedsAndWarns
    {
        const string Skip = "Full host + log sink: N1 boot-time proof — appsettings SafeScope=[] must not fail-fatal and must emit the F25.1 WARN log line naming the F4.4 degraded mode.";

        [Fact(Skip = Skip)]
        public void TheHostReachesReadyWithAnEmptySafeScopeInAppsettings() { }

        [Fact(Skip = Skip)]
        public void AWarnLogLineNamesTheF44DegradedMode() { }
    }

    // ---------------------------------------------------------------------
    // WIRE — PUT round-trip: [] returns 200 and logs (F25.2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioPutSafeScopeEmptyRoundTripSucceedsAndWarns
    {
        const string Skip = "Full settings pipeline (Story063-pattern factory): PUT [] must persist, return 200, and emit the F25.2 operator-origin WARN.";

        [Fact(Skip = Skip)]
        public void ThePutReturns200AndPersistsTheOverlay() { }

        [Fact(Skip = Skip)]
        public void AWarnLogLineNamesTheOperatorOriginAndF44DegradedMode() { }

        [Fact(Skip = Skip)]
        public void TheKeyIsReadBackAsAnEmptyListOnTheNextGet() { }
    }
}
