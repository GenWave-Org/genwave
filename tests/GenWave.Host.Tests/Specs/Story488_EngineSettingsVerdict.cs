// STORY-488 — Status says whether the engine runs the saved settings (gh-#879 · SPEC F213.4–F213.9 · PLAN T600–T602)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Entry point: GET /api/status through WebApplicationFactory with a fake IEngineTuningReader and a
// FakeTimeProvider-driven probe; WARN/INFO read from a capturing logger provider. The gw_tuning command is
// proven against real Liquidsoap (engine image) — Integration.

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineSettingsVerdict
{
    const string PendingLiq = "pending: T600 — genwave.liq gw_tuning read-only command (STORY-488)";
    const string PendingVerdict = "pending: T601 — IEngineTuningReader + verdict compute (STORY-488)";
    const string PendingStatus = "pending: T602 — probe-cached verdict on /api/status + WARN/INFO (STORY-488)";

    public sealed class ScenarioTheEngineReportsWhatItRuns
    {
        // Given: engine image started with GW_XFADE_MIN=2 GW_XFADE_MAX=8 GW_SAFE_GAP_SECONDS=7; `gw_tuning` on :1234

        /// <summary>AC1 — the reply carries all three keys with the started values</summary>
        [Fact(Skip = PendingLiq), Trait("Category", "Integration")]
        public void RepliesWithTheStartedValues() => Assert.Fail(PendingLiq);

        /// <summary>AC2 — gw_tuning is the only new server command in genwave.liq</summary>
        [Fact(Skip = PendingLiq)]
        public void OnlyOneNewCommand() => Assert.Fail(PendingLiq);

        /// <summary>AC2 — its handler ignores its argument (sets nothing)</summary>
        [Fact(Skip = PendingLiq)]
        public void TheCommandIsReadOnly() => Assert.Fail(PendingLiq);
    }

    public sealed class ScenarioEqualValues
    {
        // Given: effective settings 2 / 8 / 7.0; reader returns "GW_XFADE_MIN=2. GW_XFADE_MAX=8. GW_SAFE_GAP_SECONDS=7."

        /// <summary>AC3 — verdict is inSync</summary>
        [Fact(Skip = PendingVerdict)]
        public void InSync() => Assert.Fail(PendingVerdict);

        /// <summary>AC3 — differs is empty</summary>
        [Fact(Skip = PendingVerdict)]
        public void NothingDiffers() => Assert.Fail(PendingVerdict);
    }

    public sealed class ScenarioOneValueDiffers
    {
        // Given: effective GW_XFADE_MIN 3 (others equal); reader reports 2.

        /// <summary>AC4 — verdict is restartNeeded</summary>
        [Fact(Skip = PendingVerdict)]
        public void RestartNeeded() => Assert.Fail(PendingVerdict);

        /// <summary>AC4 — differs is exactly ["GW_XFADE_MIN"]</summary>
        [Fact(Skip = PendingVerdict)]
        public void NamesTheKey() => Assert.Fail(PendingVerdict);
    }

    public sealed class ScenarioStatusCarriesTheVerdict
    {
        // Given: WAF; fake reader reports GW_XFADE_MIN 2 against effective 3; one probe tick; GET /api/status

        /// <summary>AC5 — engine.settings is "restartNeeded"</summary>
        [Fact(Skip = PendingStatus)]
        public void Settings() => Assert.Fail(PendingStatus);

        /// <summary>AC5 — engine.differs is ["GW_XFADE_MIN"]</summary>
        [Fact(Skip = PendingStatus)]
        public void Differs() => Assert.Fail(PendingStatus);

        /// <summary>AC12 — the engine block holds only settings and differs</summary>
        [Fact(Skip = PendingStatus)]
        public void NoValuesLeak() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioStatusNeverOpensASocket
    {
        // Given: WAF; counting fake reader; one probe tick, counter reset; 5 × GET /api/status inside the interval

        /// <summary>AC6 — the reader count stays 0</summary>
        [Fact(Skip = PendingStatus)]
        public void ReaderNotCalled() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioEnteringRestartNeeded
    {
        // Given: WAF; fake reader scripted inSync → restartNeeded → restartNeeded; 3 probe ticks

        /// <summary>AC7 — exactly one WARN</summary>
        [Fact(Skip = PendingStatus)]
        public void OneWarn() => Assert.Fail(PendingStatus);

        /// <summary>AC7 — the WARN names GW_XFADE_MIN</summary>
        [Fact(Skip = PendingStatus)]
        public void WarnNamesTheKey() => Assert.Fail(PendingStatus);

        /// <summary>AC7 — the WARN names `docker compose restart engine`</summary>
        [Fact(Skip = PendingStatus)]
        public void WarnNamesTheFix() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioBackInSync
    {
        // Given: WAF; fake reader scripted restartNeeded → inSync; 2 probe ticks

        /// <summary>AC8 — exactly one INFO saying the engine settings are back in sync</summary>
        [Fact(Skip = PendingStatus)]
        public void OneInfo() => Assert.Fail(PendingStatus);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineUnreachable
    {
        // Given: reader throws (socket refused / timeout)

        /// <summary>AC9 — verdict is unknown</summary>
        [Fact(Skip = PendingVerdict)]
        public void Unknown() => Assert.Fail(PendingVerdict);

        /// <summary>AC9 — no new WARN (via WAF + one probe tick)</summary>
        [Fact(Skip = PendingStatus)]
        public void NoNewWarn() => Assert.Fail(PendingStatus);
    }

    public sealed class ScenarioGarbledReply
    {
        // Given: reader returns "GW_XFADE_MIN=abc"

        /// <summary>AC10 — verdict is unknown</summary>
        [Fact(Skip = PendingVerdict)]
        public void Unknown() => Assert.Fail(PendingVerdict);
    }

    public sealed class ScenarioBeforeTheFirstProbe
    {
        // Given: WAF just booted; no probe tick; GET /api/status

        /// <summary>AC11 — engine.settings is "unknown"</summary>
        [Fact(Skip = PendingStatus)]
        public void Unknown() => Assert.Fail(PendingStatus);

        /// <summary>AC11 — status returns 200</summary>
        [Fact(Skip = PendingStatus)]
        public void StatusIs200() => Assert.Fail(PendingStatus);
    }
}
